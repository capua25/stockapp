namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// Opciones de configuración del actualizador in-app.
/// Se registra en DI como singleton y se inyecta en <see cref="VelopackGatewayReal"/>.
/// </summary>
public sealed class UpdaterOptions
{
    /// <summary>
    /// URL del feed de actualizaciones (SimpleWebSource), derivada de la URL de la API
    /// con <see cref="UrlFeedActualizaciones.Desde"/>. Ejemplo: "https://host:8080/updates/".
    /// </summary>
    public required string FeedUrl { get; init; }
}
