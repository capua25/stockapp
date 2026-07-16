using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

/// <summary>
/// ABM de RubroGasto (los 17 rubros de la hoja Variables). El código numérico es único.
/// Baja lógica con Activo=false.
/// </summary>
public class RubroGastoService : IRubroGastoService
{
    private readonly IRubroGastoRepository _repo;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;

    public RubroGastoService(
        IRubroGastoRepository repo,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    private static void ValidarCampos(RubroGasto rubro)
    {
        if (rubro.Codigo <= 0)
            throw new ArgumentException("El código del rubro debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(rubro.Nombre))
            throw new ArgumentException("El nombre del rubro es obligatorio.");
    }

    public async Task<int> AltaAsync(RubroGasto rubro)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        ValidarCampos(rubro);

        if (await _repo.ExisteCodigoAsync(rubro.Codigo, null))
            throw new ReglaDeNegocioException($"Ya existe un rubro con el código {rubro.Codigo}.");

        var id = await _repo.AgregarAsync(rubro);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaRubroGasto,
            "RubroGasto", id,
            $"Código: {rubro.Codigo}; Nombre: {rubro.Nombre}");

        return id;
    }

    public async Task ModificarAsync(RubroGasto rubro)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        ValidarCampos(rubro);

        var original = await _repo.ObtenerPorIdAsync(rubro.Id)
            ?? throw new EntidadNoEncontradaException($"Rubro de gasto {rubro.Id} no encontrado.");

        if (original.Codigo != rubro.Codigo
            && await _repo.ExisteCodigoAsync(rubro.Codigo, rubro.Id))
            throw new ReglaDeNegocioException($"Ya existe un rubro con el código {rubro.Codigo}.");

        var cambios = new List<string>();
        if (original.Codigo != rubro.Codigo)
            cambios.Add($"Código: {original.Codigo} → {rubro.Codigo}");
        if (original.Nombre != rubro.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {rubro.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Codigo = rubro.Codigo;
        original.Nombre = rubro.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionRubroGasto,
            "RubroGasto", rubro.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);

        var rubro = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Rubro de gasto {id} no encontrado.");

        if (!rubro.Activo)
            throw new ReglaDeNegocioException($"El rubro de gasto {id} ya está inactivo.");

        rubro.Activo = false;
        await _repo.ActualizarAsync(rubro);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaRubroGasto,
            "RubroGasto", id,
            $"Baja lógica de '{rubro.Nombre}' (código {rubro.Codigo})");
    }

    public async Task<IReadOnlyList<RubroGasto>> ListarTodosAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        return await _repo.ListarTodosAsync();
    }

    public async Task<IReadOnlyList<RubroGasto>> ListarActivosAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        var todos = await _repo.ListarTodosAsync();
        return todos.Where(r => r.Activo).ToList();
    }
}
