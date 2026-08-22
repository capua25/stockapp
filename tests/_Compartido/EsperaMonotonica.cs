using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace StockApp.Tests.Compartido;

/// <summary>
/// Sondeo compartido de una condición observable, medido con reloj MONOTÓNICO (<see
/// cref="Stopwatch"/>) en vez de reloj de pared (bugfix/backups-endpoint-tests-flaky).
///
/// <c>DateTime.UtcNow</c>/<c>DateTime.Now</c> leen CLOCK_REALTIME: pueden saltar hacia adelante
/// (reajuste NTP) o hacia atrás (pausa de VM/host, NTP step negativo). Un deadline construido
/// como <c>DateTime.UtcNow.Add(timeout)</c> más un <c>while (... &lt; limite)</c> puede dispararse
/// sin que haya transcurrido tiempo real -- confirmado en este repo: un test que "agotó" un
/// timeout de 10s había tardado 1,22s reales. <see cref="Stopwatch"/> usa el contador monotónico
/// del sistema operativo (CLOCK_MONOTONIC), inmune a esos saltos.
///
/// Usá este helper SÓLO cuando no hay ninguna <see cref="Task"/> ni otra primitiva de
/// sincronización real que esperar -- si existe una señal awaitable (como
/// <c>DisparadorBackupManual.UltimaCorridaEnBackgroundParaTests</c>), preferí siempre hacer
/// <c>await</c> directo sobre ella: es estrictamente mejor, no hay timeout que ajustar ni
/// intervalo de sondeo que calibrar.
/// </summary>
public static class EsperaMonotonica
{
    private static readonly TimeSpan IntervaloPorDefecto = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// Sondea <paramref name="condicion"/> hasta que devuelva <c>true</c> o venza <paramref
    /// name="timeout"/> (medido con <see cref="Stopwatch"/>, no con reloj de pared). Devuelve el
    /// resultado final de <paramref name="condicion"/> -- no lanza por timeout; quien llama decide
    /// cómo reportar el fallo (Assert.True con mensaje, TimeoutException propio, etc.), igual que
    /// hacían los cuatro sitios que este helper reemplaza.
    /// </summary>
    public static async Task<bool> HastaAsync(
        Func<bool> condicion, TimeSpan timeout, TimeSpan? intervalo = null)
    {
        var paso = intervalo ?? IntervaloPorDefecto;
        var cronometro = Stopwatch.StartNew();

        while (!condicion())
        {
            if (cronometro.Elapsed >= timeout)
                return condicion();

            await Task.Delay(paso);
        }

        return true;
    }
}
