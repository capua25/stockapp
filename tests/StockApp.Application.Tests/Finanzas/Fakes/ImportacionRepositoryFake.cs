using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Tests.Finanzas.Fakes;

/// <summary>
/// Spy de IImportacionRepository: graba el dto/usuarioId/idImportacion con el que se lo llamó
/// y devuelve un resultado fijo pasado por constructor. Permite testear
/// ConfirmacionImportacionService (validación + enrutamiento del permiso) SIN Postgres — la
/// transacción real es Task 4, en Infrastructure.Tests.
/// </summary>
public sealed class ImportacionRepositoryFake : IImportacionRepository
{
    private readonly ResultadoConfirmacionDto _resultadoConfirmar;
    private readonly ResultadoReversionDto _resultadoRevertir;
    private readonly IReadOnlyList<ImportacionHistorialDto> _historial;

    public ConfirmarImportacionDto? DtoRecibido { get; private set; }
    public int? UsuarioIdRecibido { get; private set; }
    public Guid? IdImportacionRevertidaRecibida { get; private set; }
    public int VecesConfirmarLlamado { get; private set; }
    public int VecesRevertirLlamado { get; private set; }
    public int VecesListarHistorialLlamado { get; private set; }

    public ImportacionRepositoryFake(
        ResultadoConfirmacionDto? resultadoConfirmar = null,
        ResultadoReversionDto? resultadoRevertir = null,
        IReadOnlyList<ImportacionHistorialDto>? historial = null)
    {
        _resultadoConfirmar = resultadoConfirmar
            ?? new ResultadoConfirmacionDto(
                Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>());
        _resultadoRevertir = resultadoRevertir
            ?? new ResultadoReversionDto(Guid.NewGuid(), 0, 0, 0, 0, 0);
        _historial = historial ?? new List<ImportacionHistorialDto>();
    }

    public Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId)
    {
        DtoRecibido = dto;
        UsuarioIdRecibido = usuarioId;
        VecesConfirmarLlamado++;
        return Task.FromResult(_resultadoConfirmar);
    }

    public Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId)
    {
        IdImportacionRevertidaRecibida = idImportacion;
        UsuarioIdRecibido = usuarioId;
        VecesRevertirLlamado++;
        return Task.FromResult(_resultadoRevertir);
    }

    public Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync()
    {
        VecesListarHistorialLlamado++;
        return Task.FromResult(_historial);
    }
}
