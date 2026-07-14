namespace StockApp.Application.Reportes;

/// <summary>
/// Implementación thread-safe de <see cref="IVersionReportes"/> con un contador
/// atómico. Se registra como singleton: una sola versión para todo el proceso.
/// </summary>
public sealed class VersionReportes : IVersionReportes
{
    private long _actual;

    public long Actual => Interlocked.Read(ref _actual);

    public void Invalidar() => Interlocked.Increment(ref _actual);
}
