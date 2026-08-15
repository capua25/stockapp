using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Reproduce (FASE 1, bug reportado 2026-08-14) un freeze observado UNA sola vez en la app real
/// bajo WSLg: click en "Reabrir" en el detalle de un documento anulado congela la ventana
/// (no responde, no crashea), ANTES de mostrar el diálogo de motivo y ANTES de cualquier request
/// HTTP. La secuencia exacta que lo produjo: diálogo 1 CANCELADO sin escribir nada, diálogo 2
/// CONFIRMADO con motivo, navegación a otra vista, diálogo 3 ("Reabrir") -- congela.
///
/// Este banco monta un <see cref="Window"/> real como MainWindow (vía
/// <see cref="ClassicDesktopStyleApplicationLifetime"/>, inyectado por reflexión en
/// Application.Current porque el setter público de ApplicationLifetime tira
/// InvalidOperationException una vez completado el Setup de AppBuilder, que ya corrió para
/// cuando arranca el cuerpo de un [AvaloniaFact]) y ejercita <see cref="ConfirmacionService"/>
/// tal cual lo usa DocumentoFormViewModel.AnularAsync/ReabrirAsync: mismo Dispatcher.InvokeAsync,
/// mismo ShowDialog&lt;string?&gt; sobre el mismo owner, tres veces seguidas.
/// </summary>
public class ConfirmacionServiceDialogosConsecutivosTests
{
    private static readonly TimeSpan TimeoutEsperaDialogo = TimeSpan.FromSeconds(5);

    [AvaloniaFact]
    public async Task TresPedirTextoAsyncConsecutivos_CancelarLuegoConfirmarLuegoAbrirTercero_NoCuelga()
    {
        var owner = new Window();
        owner.Show();

        var lifetime = new ClassicDesktopStyleApplicationLifetime { MainWindow = owner };
        InyectarApplicationLifetime(lifetime);

        var svc = new ConfirmacionService();

        // Diálogo 1: se cancela sin escribir nada (paso 3 de la secuencia real).
        var task1 = svc.PedirTextoAsync("Anular documento", "Ingresá el motivo de la anulación:");
        var dialog1 = await EsperarDialogoAsync(owner);
        dialog1.Close(null);
        var motivo1 = await EsperarResultadoAsync(task1, "diálogo 1 (cancelar)");
        Assert.Null(motivo1);

        // Diálogo 2: se confirma con motivo (paso 4 de la secuencia real).
        var task2 = svc.PedirTextoAsync("Anular documento", "Ingresá el motivo de la anulación:");
        var dialog2 = await EsperarDialogoAsync(owner);
        dialog2.Close("Motivo real de anulación");
        var motivo2 = await EsperarResultadoAsync(task2, "diálogo 2 (confirmar)");
        Assert.Equal("Motivo real de anulación", motivo2);

        // Diálogo 3: es el que se congela en producción (paso 6, "Reabrir").
        var task3 = svc.PedirTextoAsync("Reabrir documento", "Ingresá el motivo de la reapertura:");
        var dialog3 = await EsperarDialogoAsync(owner);
        dialog3.Close("Motivo real de reapertura");
        var motivo3 = await EsperarResultadoAsync(task3, "diálogo 3 (el que se cuelga en producción)");
        Assert.Equal("Motivo real de reapertura", motivo3);
    }

    private static void InyectarApplicationLifetime(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var campo = typeof(Avalonia.Application).GetField(
            "_applicationLifetime", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(campo);
        campo!.SetValue(Avalonia.Application.Current, lifetime);
    }

    /// <summary>
    /// Espera (con timeout explícito) a que el diálogo modal aparezca como hijo de <paramref name="owner"/>.
    /// Si el bug se reproduce, esto es lo primero que falla: el diálogo nunca llega a crearse/mostrarse.
    /// </summary>
    private static async Task<Window> EsperarDialogoAsync(Window owner)
    {
        var inicio = DateTime.UtcNow;
        while (DateTime.UtcNow - inicio < TimeoutEsperaDialogo)
        {
            if (owner.OwnedWindows.Count > 0)
                return owner.OwnedWindows[0];
            await Task.Delay(10);
        }

        throw new TimeoutException(
            $"El diálogo nunca se creó/mostró como hijo de la ventana principal (timeout {TimeoutEsperaDialogo}).");
    }

    /// <summary>
    /// Espera (con timeout explícito) a que el Task de PedirTextoAsync resuelva tras cerrar el
    /// diálogo. Si el bug se reproduce acá en vez de en EsperarDialogoAsync, el diálogo se
    /// mostró pero cerrarlo no destraba el ShowDialog subyacente.
    /// </summary>
    private static async Task<string?> EsperarResultadoAsync(Task<string?> task, string contexto)
    {
        var completada = await Task.WhenAny(task, Task.Delay(TimeoutEsperaDialogo));
        if (completada != task)
            throw new TimeoutException($"Se colgó esperando el resultado de {contexto} (timeout {TimeoutEsperaDialogo}).");

        return await task;
    }
}
