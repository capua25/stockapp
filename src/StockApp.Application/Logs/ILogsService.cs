namespace StockApp.Application.Logs;

/// <summary>
/// Contrato que consume el desktop. Lo implementa <c>LogsApiClient</c> contra <c>/logs</c>.
/// </summary>
public interface ILogsService
{
    Task<ResumenLogsDto> ObtenerResumenAsync(CancellationToken ct = default);
    Task<LogsDescargaDto> DescargarZipAsync(CancellationToken ct = default);
}
