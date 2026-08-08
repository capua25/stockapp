using StockApp.Api.Backups;
using Xunit;

namespace StockApp.Api.Tests.Backups;

/// <summary>
/// Unit test puro (sin HTTP): la garantía real que necesitamos -- "nunca dos corridas
/// simultáneas" -- es una propiedad del SemaphoreSlim(1,1), no de la infraestructura HTTP.
/// Probarla acá es determinístico (Wait(0) es atómico); un test de integración vía HTTP
/// (BackupsEndpointTests) cubre además que el endpoint realmente usa esta guardia.
/// </summary>
public class GuardiaCorridaBackupTests
{
    [Fact]
    public void TryEntrar_SinNadieAdentro_DevuelveTrue()
    {
        var guardia = new GuardiaCorridaBackup();

        Assert.True(guardia.TryEntrar());
    }

    [Fact]
    public void TryEntrar_ConAlguienYaAdentro_DevuelveFalse()
    {
        var guardia = new GuardiaCorridaBackup();
        Assert.True(guardia.TryEntrar());

        Assert.False(guardia.TryEntrar());
    }

    [Fact]
    public void TryEntrar_DespuesDeSalir_VuelveADevolverTrue()
    {
        var guardia = new GuardiaCorridaBackup();
        Assert.True(guardia.TryEntrar());
        guardia.Salir();

        Assert.True(guardia.TryEntrar());
    }

    [Fact]
    public async Task TryEntrar_DosLlamadasConcurrentes_ExactamenteUnaGana()
    {
        // No es una cuestión de timing: SemaphoreSlim(1,1).Wait(0) es atómico, así que esto
        // es determinístico en cada corrida, no un test flaky disfrazado.
        var guardia = new GuardiaCorridaBackup();

        var resultados = await Task.WhenAll(
            Task.Run(() => guardia.TryEntrar()),
            Task.Run(() => guardia.TryEntrar()));

        Assert.Single(resultados, r => r);
        Assert.Single(resultados, r => !r);
    }
}
