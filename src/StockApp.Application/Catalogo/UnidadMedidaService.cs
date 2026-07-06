using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de UnidadMedida. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// Unicidad por Nombre y por Abreviatura.
/// </summary>
public class UnidadMedidaService : IUnidadMedidaService
{
    // Nombre/abreviatura de la unidad de medida sembrada por defecto (ver GarantizarUnidadPorDefectoAsync).
    private const string NombreUnidadPorDefecto      = "Unidad";
    private const string AbreviaturaUnidadPorDefecto = "u";

    private readonly IUnidadMedidaRepository _repo;
    private readonly ICurrentSession         _session;
    private readonly IAuthorizationService   _auth;
    private readonly IAuditLogger            _audit;

    public UnidadMedidaService(
        IUnidadMedidaRepository repo,
        ICurrentSession         session,
        IAuthorizationService   auth,
        IAuditLogger            audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(UnidadMedida unidadMedida)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(unidadMedida.Nombre))
            throw new ArgumentException("El nombre de la unidad de medida es obligatorio.");
        if (string.IsNullOrWhiteSpace(unidadMedida.Abreviatura))
            throw new ArgumentException("La abreviatura de la unidad de medida es obligatoria.");

        if (await _repo.ExisteNombreAsync(unidadMedida.Nombre, null))
            throw new InvalidOperationException($"Ya existe una unidad de medida con el nombre '{unidadMedida.Nombre}'.");

        if (await _repo.ExisteAbreviaturaAsync(unidadMedida.Abreviatura, null))
            throw new InvalidOperationException($"Ya existe una unidad de medida con la abreviatura '{unidadMedida.Abreviatura}'.");

        var id = await _repo.AgregarAsync(unidadMedida);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaUnidadMedida,
            "UnidadMedida", id,
            $"Nombre: {unidadMedida.Nombre}, Abreviatura: {unidadMedida.Abreviatura}");

        return id;
    }

    public async Task ModificarAsync(UnidadMedida unidadMedida)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(unidadMedida.Id)
            ?? throw new KeyNotFoundException($"UnidadMedida {unidadMedida.Id} no encontrada.");

        if (original.Nombre != unidadMedida.Nombre
            && await _repo.ExisteNombreAsync(unidadMedida.Nombre, unidadMedida.Id))
            throw new InvalidOperationException($"Ya existe una unidad de medida con el nombre '{unidadMedida.Nombre}'.");

        if (original.Abreviatura != unidadMedida.Abreviatura
            && await _repo.ExisteAbreviaturaAsync(unidadMedida.Abreviatura, unidadMedida.Id))
            throw new InvalidOperationException($"Ya existe una unidad de medida con la abreviatura '{unidadMedida.Abreviatura}'.");

        var cambios = new List<string>();
        if (original.Nombre       != unidadMedida.Nombre)       cambios.Add($"Nombre: {original.Nombre} → {unidadMedida.Nombre}");
        if (original.Abreviatura  != unidadMedida.Abreviatura)  cambios.Add($"Abreviatura: {original.Abreviatura} → {unidadMedida.Abreviatura}");

        if (cambios.Count == 0)
            return;

        original.Nombre      = unidadMedida.Nombre;
        original.Abreviatura = unidadMedida.Abreviatura;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionUnidadMedida,
            "UnidadMedida", unidadMedida.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);

        var unidadMedida = await _repo.ObtenerPorIdAsync(id)
            ?? throw new KeyNotFoundException($"UnidadMedida {id} no encontrada.");

        if (!unidadMedida.Activo)
            throw new InvalidOperationException($"La unidad de medida {id} ya está inactiva.");

        unidadMedida.Activo = false;
        await _repo.ActualizarAsync(unidadMedida);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaUnidadMedida,
            "UnidadMedida", id,
            $"Baja lógica de '{unidadMedida.Nombre}' ({unidadMedida.Abreviatura})");
    }

    public async Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<UnidadMedida>> ListarActivasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(u => u.Activo).ToList();
    }

    public async Task<UnidadMedida> GarantizarUnidadPorDefectoAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarProductos);

        var todas = await _repo.ListarTodasAsync();
        var existente = todas.FirstOrDefault(u =>
            string.Equals(u.Nombre, NombreUnidadPorDefecto, StringComparison.OrdinalIgnoreCase));
        if (existente is not null)
            return existente;

        var nueva = new UnidadMedida
        {
            Nombre      = NombreUnidadPorDefecto,
            Abreviatura = AbreviaturaUnidadPorDefecto,
        };
        var id = await _repo.AgregarAsync(nueva);
        nueva.Id = id;

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaUnidadMedida,
            "UnidadMedida", id,
            $"Alta automática (seed por defecto) — Nombre: {nueva.Nombre}, Abreviatura: {nueva.Abreviatura}");

        return nueva;
    }
}
