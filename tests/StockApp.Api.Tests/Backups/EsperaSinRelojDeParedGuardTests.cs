using Xunit;

namespace StockApp.Api.Tests.Backups;

/// <summary>
/// Guardián estructural (bugfix/backups-endpoint-tests-flaky): impide que alguien reintroduzca en
/// BackupsEndpointTests un sondeo de reloj de pared (<c>while (DateTime.UtcNow ...)</c>) para medir
/// cuánto tardó un trabajo en background. DateTime.UtcNow/DateTime.Now son reloj de PARED
/// (CLOCK_REALTIME), no monotónico -- bajo contención de host pueden saltar hacia adelante y
/// disparar un timeout espurio aunque el trabajo real haya tardado milisegundos. EsperarCorridaAsync
/// se sincroniza en cambio con la Task real (DisparadorBackupManual.UltimaCorridaEnBackgroundParaTests),
/// sin reloj de por medio.
///
/// Deliberadamente NO es un regex sobre el archivo completo: una alternación con RegexOptions.Singleline
/// puede volverse voraz sobre comentarios de documentación (<c>///...</c>) y tragarse el archivo entero
/// sin fallar nunca -- antipatrón ya conocido en este proyecto. Usa comparación de substring simple
/// sobre el texto crudo del archivo, buscando específicamente el patrón peligroso ("while" + reloj de
/// pared en la misma expresión), NO cualquier mención suelta de DateTime.UtcNow -- que aparece
/// legítimamente en comentarios de este mismo archivo (documentando el bug arreglado) y como timestamp
/// de negocio en otros tests. Verificado por mutación: reintroducir el bucle viejo pone este test en
/// rojo (ver bugfix/backups-endpoint-tests-flaky).
/// </summary>
public class EsperaSinRelojDeParedGuardTests
{
    [Fact]
    public void BackupsEndpointTests_NoSondeaRelojDeParedEnBuclesDeEspera()
    {
        var ruta = ResolverRutaBackupsEndpointTests();
        var codigo = File.ReadAllText(ruta);

        var patronesProhibidos = new[]
        {
            "while (DateTime.UtcNow", "while(DateTime.UtcNow",
            "while (DateTime.Now", "while(DateTime.Now",
        };

        foreach (var patron in patronesProhibidos)
            Assert.DoesNotContain(patron, codigo);
    }

    /// <summary>Sube desde el directorio de build (bin/Debug/netX.0/...) hasta encontrar el .cs
    /// fuente en el árbol del repo -- lee el archivo real, no una copia en el output de build.</summary>
    private static string ResolverRutaBackupsEndpointTests()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "BackupsEndpointTests.cs")))
            directorio = directorio.Parent;

        if (directorio is null)
        {
            throw new InvalidOperationException(
                "No se encontró BackupsEndpointTests.cs subiendo desde el directorio de build.");
        }

        return Path.Combine(directorio.FullName, "BackupsEndpointTests.cs");
    }
}
