using System;
using System.Threading;
using System.Threading.Tasks;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Debounce de una recarga: cada <see cref="Programar"/> cancela la espera pendiente y programa una nueva, así una ráfaga
/// de cambios (un año de cuatro cifras, varios filtros que se reponen juntos) consulta una sola vez, con el último valor.
/// Solo hace el debounce: el turno de la consulta (<see cref="ConsultaVigente"/>) lo pide el método de carga del
/// ViewModel, que también usan caminos sin debounce (la carga inicial, Reintentar, Actualizar, Buscar).
/// </summary>
public sealed class RecargaConDebounce
{
    private readonly Func<TimeSpan> _espera;
    private CancellationTokenSource? _cts;

    /// <param name="espera">Se lee en cada <see cref="Programar"/>: los tests la cambian después de construir el ViewModel.</param>
    public RecargaConDebounce(Func<TimeSpan> espera) => _espera = espera;

    /// <summary>
    /// Cancela y libera la espera pendiente (si la hay) y programa una nueva. Devuelve la tarea de esa espera: si vence
    /// sin cancelarse corre <paramref name="recarga"/>; si se cancela, termina sin error.
    /// </summary>
    public Task Programar(Func<Task> recarga)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return EsperarYRecargarAsync(recarga, _espera(), _cts.Token);
    }

    private static async Task EsperarYRecargarAsync(Func<Task> recarga, TimeSpan espera, CancellationToken ct)
    {
        try
        {
            await Task.Delay(espera, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        await recarga();
    }
}
