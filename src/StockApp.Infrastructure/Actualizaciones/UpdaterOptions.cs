namespace StockApp.Infrastructure.Actualizaciones;

/// <summary>
/// Opciones de configuración del actualizador in-app.
/// Se registra en DI como singleton y se inyecta en <see cref="VelopackGatewayReal"/>.
/// </summary>
public sealed class UpdaterOptions
{
    /// <summary>
    /// URL del feed propio (SimpleWebSource). Ejemplo: "https://releases.miapp.com/stable/".
    /// Si es null o vacío, esa fuente se omite de la cadena.
    /// </summary>
    public string? FeedPropiUrl { get; init; }

    /// <summary>
    /// URL del repositorio de GitHub. Ejemplo: "https://github.com/usuario/stockapp".
    /// Requerido para <see cref="Velopack.Sources.GithubSource"/>.
    /// </summary>
    public string GitHubRepoUrl { get; init; } = string.Empty;

    /// <summary>
    /// Token de acceso de GitHub (opcional). Si es null, se aplican rate limits anónimos.
    /// </summary>
    public string? GitHubAccessToken { get; init; }

    /// <summary>
    /// Si true, incluye pre-releases de GitHub en la búsqueda.
    /// </summary>
    public bool GitHubPrerelease { get; init; } = false;

    /// <summary>
    /// Orden de fuentes para la cadena de fallback.
    /// Decisión: GitHub es primaria real; FeedPropio es fallback si está configurado.
    /// El orden está fijo: [GitHub, FeedPropio?]. Se ignora FeedPropio si su URL es nula.
    /// </summary>
    public OrdenFuentes Orden { get; init; } = OrdenFuentes.GitHubPrimero;
}

/// <summary>Orden de evaluación de las fuentes de actualización.</summary>
public enum OrdenFuentes
{
    /// <summary>GitHub primero, feed propio como fallback (default).</summary>
    GitHubPrimero,

    /// <summary>Feed propio primero, GitHub como fallback.</summary>
    FeedPropioPrimero,
}
