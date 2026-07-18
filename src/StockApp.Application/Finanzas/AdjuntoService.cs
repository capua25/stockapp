using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

public class AdjuntoService : IAdjuntoService
{
    private readonly IAdjuntoRepository    _repo;
    private readonly IGastoRepository      _gastos;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;

    public AdjuntoService(
        IAdjuntoRepository repo,
        IGastoRepository gastos,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo    = repo;
        _gastos  = gastos;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var gasto = await _gastos.ObtenerPorIdAsync(gastoId)
            ?? throw new EntidadNoEncontradaException($"No existe el gasto {gastoId}.");

        AdjuntoValidador.Validar(contenido, nombreArchivo);

        var adjunto = new Adjunto
        {
            NombreArchivo = nombreArchivo,
            ContentType = AdjuntoValidador.DetectarContentType(contenido)!,
            TamanoBytes = contenido.LongLength,
            GastoId = gasto.Id,
            FechaAltaUtc = DateTime.UtcNow,
        };

        var id = await _repo.AgregarAsync(adjunto, contenido);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaAdjunto, "Adjunto", id,
            $"Gasto {gastoId} — {nombreArchivo}");

        return ADto(adjunto);
    }

    public async Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarPagos);

        AdjuntoValidador.Validar(contenido, nombreArchivo);

        var adjunto = new Adjunto
        {
            NombreArchivo = nombreArchivo,
            ContentType = AdjuntoValidador.DetectarContentType(contenido)!,
            TamanoBytes = contenido.LongLength,
            PagoGastoId = pagoGastoId,
            FechaAltaUtc = DateTime.UtcNow,
        };

        var id = await _repo.AgregarAsync(adjunto, contenido);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaAdjunto, "Adjunto", id,
            $"Pago {pagoGastoId} — {nombreArchivo}");

        return ADto(adjunto);
    }

    public async Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        return (await _repo.ListarPorGastoAsync(gastoId)).Select(ADto).ToList();
    }

    public async Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        return (await _repo.ListarPorPagoAsync(pagoGastoId)).Select(ADto).ToList();
    }

    public async Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);

        var adjunto = await _repo.ObtenerPorIdAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        // Baja lógica: un adjunto inactivo no debe seguir siendo descargable por id, aunque
        // ObtenerPorIdAsync (usado también por QuitarAsync para re-bajas idempotentes) no
        // filtre Activo. El filtro va acá, en la ruta de descarga, no en el repo.
        if (!adjunto.Activo)
            throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        var contenido = await _repo.ObtenerContenidoAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el contenido del adjunto {adjuntoId}.");

        return new AdjuntoContenidoDto(adjunto.NombreArchivo, adjunto.ContentType, contenido);
    }

    public async Task QuitarAsync(int adjuntoId)
    {
        var adjunto = await _repo.ObtenerPorIdAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        // El permiso depende de a qué pertenece el adjunto (spec F3, decisión 2).
        _auth.Verificar(_session.RolActual, adjunto.EsDePago ? Permisos.RegistrarPagos : Permisos.RegistrarGastos);

        adjunto.Activo = false;
        await _repo.ActualizarAsync(adjunto);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.BajaAdjunto, "Adjunto", adjuntoId,
            $"{adjunto.NombreArchivo}");
    }

    private static AdjuntoDto ADto(Adjunto a) => new(
        a.Id, a.NombreArchivo, a.ContentType, a.TamanoBytes, a.GastoId, a.PagoGastoId, a.FechaAltaUtc);
}
