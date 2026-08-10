namespace StockApp.Application.Alertas;

/// <summary>Contrato que consume el desktop; lo implementa ConfiguracionAlertasApiClient sobre HTTP.</summary>
public interface IConfiguracionAlertasService
{
    Task<ConfiguracionAlertasDto> ObtenerAsync(CancellationToken ct = default);
    Task GuardarAsync(string? urlWebhook, bool habilitado, CancellationToken ct = default);
    /// <summary>
    /// <paramref name="urlWebhook"/> opcional: la URL que el usuario tiene EN PANTALLA. Si viene,
    /// el servidor prueba esa (validándola igual que al guardar) sin persistirla; si no, prueba
    /// la guardada.
    /// </summary>
    Task<ResultadoPruebaAlertaDto> ProbarAsync(string? urlWebhook = null, CancellationToken ct = default);
}
