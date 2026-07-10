using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de Proveedor. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// Unicidad por Nombre (activos e inactivos).
/// </summary>
public class ProveedorService : IProveedorService
{
    private readonly IProveedorRepository   _repo;
    private readonly ICurrentSession        _session;
    private readonly IAuthorizationService  _auth;
    private readonly IAuditLogger           _audit;

    public ProveedorService(
        IProveedorRepository  repo,
        ICurrentSession       session,
        IAuthorizationService auth,
        IAuditLogger          audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(Proveedor proveedor)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(proveedor.Nombre))
            throw new ArgumentException("El nombre del proveedor es obligatorio.");

        if (await _repo.ExisteNombreAsync(proveedor.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe un proveedor con el nombre '{proveedor.Nombre}'.");

        var id = await _repo.AgregarAsync(proveedor);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaProveedor,
            "Proveedor", id,
            $"Alta de '{proveedor.Nombre}'");

        return id;
    }

    public async Task ModificarAsync(Proveedor proveedor)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(proveedor.Id)
            ?? throw new EntidadNoEncontradaException($"Proveedor {proveedor.Id} no encontrado.");

        if (original.Nombre != proveedor.Nombre
            && await _repo.ExisteNombreAsync(proveedor.Nombre, proveedor.Id))
            throw new ReglaDeNegocioException($"Ya existe un proveedor con el nombre '{proveedor.Nombre}'.");

        var cambios = new List<string>();
        if (original.Nombre    != proveedor.Nombre)    cambios.Add($"Nombre: {original.Nombre} → {proveedor.Nombre}");
        if (original.Telefono  != proveedor.Telefono)  cambios.Add($"Telefono: {original.Telefono} → {proveedor.Telefono}");
        if (original.Email     != proveedor.Email)     cambios.Add($"Email: {original.Email} → {proveedor.Email}");
        if (original.Direccion != proveedor.Direccion) cambios.Add($"Direccion: {original.Direccion} → {proveedor.Direccion}");
        if (original.Notas     != proveedor.Notas)     cambios.Add($"Notas: {original.Notas} → {proveedor.Notas}");

        if (cambios.Count == 0)
            return;

        original.Nombre    = proveedor.Nombre;
        original.Telefono  = proveedor.Telefono;
        original.Email     = proveedor.Email;
        original.Direccion = proveedor.Direccion;
        original.Notas     = proveedor.Notas;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionProveedor,
            "Proveedor", proveedor.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);

        var proveedor = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Proveedor {id} no encontrado.");

        if (!proveedor.Activo)
            throw new ReglaDeNegocioException($"El proveedor {id} ya está inactivo.");

        proveedor.Activo = false;
        await _repo.ActualizarAsync(proveedor);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaProveedor,
            "Proveedor", id,
            $"Baja lógica de '{proveedor.Nombre}'");
    }

    public async Task<IReadOnlyList<Proveedor>> ListarTodosAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodosAsync();
    }
}
