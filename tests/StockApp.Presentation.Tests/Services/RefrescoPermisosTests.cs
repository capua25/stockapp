using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

public class RefrescoPermisosTests
{
    [Fact]
    public async Task DispararBestEffortAsync_OperacionExitosa_LaEjecuta()
    {
        var ejecutada = false;

        await RefrescoPermisos.DispararBestEffortAsync(
            () => { ejecutada = true; return Task.CompletedTask; }, "test");

        Assert.True(ejecutada);
    }

    [Fact]
    public async Task DispararBestEffortAsync_OperacionLanzaSincronicamente_NoPropagaLaExcepcion()
    {
        var ex = await Record.ExceptionAsync(() =>
            RefrescoPermisos.DispararBestEffortAsync(
                () => throw new InvalidOperationException("boom"), "test"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task DispararBestEffortAsync_OperacionLanzaAsincronicamente_NoPropagaLaExcepcion()
    {
        var ex = await Record.ExceptionAsync(() =>
            RefrescoPermisos.DispararBestEffortAsync(
                async () => { await Task.Yield(); throw new InvalidOperationException("boom"); }, "test"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task DispararBestEffortAsync_DevuelveElTaskQueEnvuelveLaOperacion_ParaSincronizacionDeterministaEnTests()
    {
        // Este es el contrato que consume PanelPermisosViewModel (Task 13, corrección A): el
        // Task devuelto completa DESPUÉS de que la operación (exitosa o no) terminó, nunca
        // antes — es lo que permite awaitarlo desde un test sin Task.Delay.
        var ordenDeEjecucion = new List<string>();

        var tarea = RefrescoPermisos.DispararBestEffortAsync(async () =>
        {
            await Task.Yield();
            ordenDeEjecucion.Add("operacion");
        }, "test");
        await tarea;
        ordenDeEjecucion.Add("despues-del-await");

        Assert.Equal(new[] { "operacion", "despues-del-await" }, ordenDeEjecucion);
    }
}
