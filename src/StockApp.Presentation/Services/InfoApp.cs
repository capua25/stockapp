using System.Reflection;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de <see cref="IInfoApp"/>. Lee la versión desde
/// <see cref="AssemblyInformationalVersionAttribute"/> (que en el csproj es $(Version)).
/// </summary>
public sealed class InfoApp : IInfoApp
{
    private const string VersionFallback = "0.0.0";

    private readonly string _version;

    public InfoApp()
    {
        var informational = typeof(InfoApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        _version = Normalizar(informational);
    }

    /// <inheritdoc />
    public string Version => _version;

    /// <summary>
    /// Normaliza el valor crudo del atributo: descarta metadata de build posterior al '+'
    /// (ej. "0.1.1+abc123" → "0.1.1") y devuelve un fallback si es null/vacío.
    /// </summary>
    internal static string Normalizar(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
            return VersionFallback;

        var indicePlus = informational.IndexOf('+');
        return indicePlus >= 0
            ? informational[..indicePlus]
            : informational;
    }
}
