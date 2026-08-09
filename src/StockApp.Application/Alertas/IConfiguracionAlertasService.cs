namespace StockApp.Application.Alertas;

/// <summary>Contrato que consume el desktop; lo implementa ConfiguracionAlertasApiClient sobre HTTP.</summary>
public interface IConfiguracionAlertasService
{
    Task<ConfiguracionAlertasDto> ObtenerAsync(CancellationToken ct = default);
    Task GuardarAsync(string? urlWebhook, bool habilitado, CancellationToken ct = default);
    Task<ResultadoPruebaAlertaDto> ProbarAsync(CancellationToken ct = default);
}
