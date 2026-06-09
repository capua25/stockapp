using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de Categoria. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// </summary>
public class CategoriaService : ICategoriaService
{
    private readonly ICategoriaRepository   _repo;
    private readonly ICurrentSession        _session;
    private readonly IAuthorizationService  _auth;
    private readonly IAuditLogger           _audit;

    public CategoriaService(
        ICategoriaRepository  repo,
        ICurrentSession       session,
        IAuthorizationService auth,
        IAuditLogger          audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(Categoria categoria)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(categoria.Nombre))
            throw new ArgumentException("El nombre de la categoría es obligatorio.");

        if (await _repo.ExisteNombreAsync(categoria.Nombre, null))
            throw new InvalidOperationException($"Ya existe una categoría con el nombre '{categoria.Nombre}'.");

        var id = await _repo.AgregarAsync(categoria);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaCategoria,
            "Categoria", id,
            $"Nombre: {categoria.Nombre}");

        return id;
    }

    public async Task ModificarAsync(Categoria categoria)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(categoria.Id)
            ?? throw new KeyNotFoundException($"Categoría {categoria.Id} no encontrada.");

        if (original.Nombre != categoria.Nombre
            && await _repo.ExisteNombreAsync(categoria.Nombre, categoria.Id))
            throw new InvalidOperationException($"Ya existe una categoría con el nombre '{categoria.Nombre}'.");

        var cambios = new List<string>();
        if (original.Nombre != categoria.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {categoria.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = categoria.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionCategoria,
            "Categoria", categoria.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);

        var categoria = await _repo.ObtenerPorIdAsync(id)
            ?? throw new KeyNotFoundException($"Categoría {id} no encontrada.");

        if (!categoria.Activo)
            throw new InvalidOperationException($"La categoría {id} ya está inactiva.");

        categoria.Activo = false;
        await _repo.ActualizarAsync(categoria);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaCategoria,
            "Categoria", id,
            $"Baja lógica de '{categoria.Nombre}'");
    }

    public async Task<IReadOnlyList<Categoria>> ListarTodasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodasAsync();
    }
}
