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
/// Guardián de cobertura, NO reproductor de bug. Nació intentando reproducir un freeze
/// reportado el 2026-08-14 en el módulo de documentos administrativos (click en "Reabrir"
/// congelaba la ventana en la app real bajo WSLg); ese reporte se declaró NO reproducible el
/// 2026-08-15 tras 3 corridas manuales (incluida la secuencia original exacta), este mismo
/// arnés en verde validado por mutación, y la decompilación de Avalonia 12.0.5 confirmando que
/// <c>Window.ShowDialog&lt;T&gt;</c> es async puro (TaskCompletionSource, sin PushFrame) — la
/// causa probable fue contaminación del entorno WSLg con múltiples actores sobre la misma
/// ventana X11, no un bug de la app.
///
/// Lo que cubre en concreto: es el único test que monta un <see cref="Window"/> real como
/// MainWindow y encadena tres <see cref="ConfirmacionService.PedirTextoAsync"/> reales sobre el
/// mismo owner, tal cual lo usa DocumentoFormViewModel.AnularAsync/ReabrirAsync (mismo
/// Dispatcher.InvokeAsync, mismo ShowDialog&lt;string?&gt; sobre el mismo owner, tres veces
/// seguidas). El <c>ConfirmacionServiceTests.cs</c> de Presentation.Tests solo ejercita el
/// camino defensivo con Application.Current sin inicializar; no hay otro test que llegue a
/// mostrar un diálogo real.
///
/// Deuda conocida: <see cref="InyectarApplicationLifetime"/> inyecta por reflexión el campo
/// privado <c>Avalonia.Application._applicationLifetime</c> porque el setter público de
/// ApplicationLifetime tira InvalidOperationException una vez completado el Setup de AppBuilder
/// (que ya corrió para cuando arranca el cuerpo de un [AvaloniaFact]). Si un bump de Avalonia
/// renombra o elimina ese campo privado, este test se rompe por eso, no por una regresión real.
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
