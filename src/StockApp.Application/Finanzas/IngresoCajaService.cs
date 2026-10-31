using System.Globalization;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Domain.Formato;

namespace StockApp.Application.Finanzas;

public class IngresoCajaService : IIngresoCajaService
{
    public const string MensajeCajaObligatoria = "La caja del ingreso es obligatoria.";

    private readonly IIngresoCajaRepository          _repo;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public IngresoCajaService(
        IIngresoCajaRepository repo,
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

    private async Task ValidarAsync(IngresoCaja ingreso, IngresoCaja? original)
    {
        if (string.IsNullOrWhiteSpace(ingreso.Concepto))
            throw new ArgumentException("El concepto del ingreso es obligatorio.");
        if (ingreso.Monto <= 0)
            throw new ArgumentException("El monto del ingreso debe ser mayor a cero.");
        if (ingreso.Fecha == default)
            throw new ArgumentException("La fecha del ingreso es obligatoria.");

        var cambio = original is null
            || original.FuenteFinanciamientoId != ingreso.FuenteFinanciamientoId
            || original.Caja != ingreso.Caja;
        await CajaYLiteral.ValidarFuenteAsync(_fuentes, ingreso.Caja, ingreso.FuenteFinanciamientoId, cambio);
    }

    public async Task<int> AltaAsync(IngresoCaja ingreso)
    {
        _auth.Verificar(_session, Permisos.RegistrarIngresos);
        await ValidarAsync(ingreso, original: null);

        var id = await _repo.AgregarAsync(ingreso);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaIngresoCaja, "IngresoCaja", id,
            $"Concepto: {ingreso.Concepto}; Monto: {FormatoEsUy.Moneda(ingreso.Monto)}; Fecha: {TextoFecha(ingreso.Fecha)}; " +
            $"Caja: {ingreso.Caja.Nombre()}; Fuente: {await NombreFuenteAsync(ingreso.FuenteFinanciamientoId)}");

        return id;
    }

    public async Task ModificarAsync(IngresoCaja ingreso)
    {
        _auth.Verificar(_session, Permisos.RegistrarIngresos);

        var original = await _repo.ObtenerPorIdAsync(ingreso.Id)
            ?? throw new EntidadNoEncontradaException($"Ingreso de caja {ingreso.Id} no encontrado.");
        if (!original.Activo)
            throw new ReglaDeNegocioException("No se puede modificar un ingreso dado de baja.");

        await ValidarAsync(ingreso, original);

        var cambios = new List<string>();
        if (original.Concepto != ingreso.Concepto)
            cambios.Add($"Concepto: {original.Concepto} → {ingreso.Concepto}");
        if (original.Fecha != ingreso.Fecha)
            cambios.Add($"Fecha: {TextoFecha(original.Fecha)} → {TextoFecha(ingreso.Fecha)}");
        if (original.Monto != ingreso.Monto)
            cambios.Add($"Monto: {FormatoEsUy.Moneda(original.Monto)} → {FormatoEsUy.Moneda(ingreso.Monto)}");
        if (original.Caja != ingreso.Caja)
            cambios.Add($"Caja: {original.Caja.Nombre()} → {ingreso.Caja.Nombre()}");
        if (original.FuenteFinanciamientoId != ingreso.FuenteFinanciamientoId)
            cambios.Add($"Fuente: {await NombreFuenteAsync(original.FuenteFinanciamientoId)} → {await NombreFuenteAsync(ingreso.FuenteFinanciamientoId)}");

        if (cambios.Count == 0)
            return;

        // Solo la FK, no la nav (mismo criterio que GastoService: el fixup de EF
        // daría vuelta el FK si la nav tracked se pone en null).
        original.Concepto               = ingreso.Concepto;
        original.Fecha                  = ingreso.Fecha;
        original.Monto                  = ingreso.Monto;
        original.Caja                   = ingreso.Caja;
        original.FuenteFinanciamientoId = ingreso.FuenteFinanciamientoId;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.ModificacionIngresoCaja, "IngresoCaja", ingreso.Id,
            string.Join("; ", cambios));
    }

    // Detalle legible para la pantalla de Auditoría: fecha es-UY y la fuente por nombre.
    private static string TextoFecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private async Task<string> NombreFuenteAsync(int id) =>
        (await _fuentes.ObtenerPorIdAsync(id))?.Nombre ?? $"fuente {id}";

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session, Permisos.RegistrarIngresos);

        var ingreso = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Ingreso de caja {id} no encontrado.");
        if (!ingreso.Activo)
            throw new ReglaDeNegocioException($"El ingreso de caja {id} ya está dado de baja.");

        ingreso.Activo = false;
        await _repo.ActualizarAsync(ingreso);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.BajaIngresoCaja, "IngresoCaja", id,
            $"Baja de '{ingreso.Concepto}' (monto {FormatoEsUy.Moneda(ingreso.Monto)})");
    }

    public async Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync()
    {
        _auth.Verificar(_session, Permisos.VerFinanzas);
        return await _repo.ListarTodosAsync();
    }
}
