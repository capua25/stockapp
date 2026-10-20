using System;
using System.Threading;
using System.Threading.Tasks;

namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// Espera entre dos chequeos periódicos. Existe para que los tests avancen el tiempo sin reloj real.
/// </summary>
public interface IEsperaChequeo
{
    Task EsperarAsync(TimeSpan intervalo, CancellationToken ct);
}

/// <summary>Espera real: <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
public sealed class EsperaChequeoReal : IEsperaChequeo
{
    public Task EsperarAsync(TimeSpan intervalo, CancellationToken ct)
        => Task.Delay(intervalo, ct);
}
