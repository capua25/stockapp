namespace StockApp.UiV2.Herramientas.Tests;

public class CliTests
{
    [Fact]
    public void Ejecutar_ComandoDesconocido_Devuelve2YMuestraElUso()
    {
        var salida = new StringWriter();
        var error = new StringWriter();

        var codigo = Cli.Ejecutar(["no-existe"], salida, error);

        Assert.Equal(2, codigo);
        Assert.Contains("Uso: dotnet run --project tools/StockApp.UiV2.Herramientas", error.ToString());
    }

    /// <summary>
    /// Cierra la deuda de la Task 1 (H6): el catch de Ejecutar no tenía un test propio que
    /// probara que una excepción real se convierte en exit code 1 con el mensaje en stderr, en
    /// vez de escapar. "tokens" es el primer comando que puede fallar así: con una ruta de
    /// tokens.json inexistente, LectorTokens.LeerArchivo dispara FileNotFoundException al leer
    /// el archivo. Usa el parámetro rutaTokensJson (solo para tests) para no tocar el
    /// docs/ui-v2/tokens.json real: ese archivo está trackeado en git y ningún test debe
    /// borrarlo ni escribirlo, ni siquiera transitoriamente.
    /// </summary>
    [Fact]
    public void Ejecutar_ComandoTokensSinArchivo_Devuelve1YElMensajeEnStderr()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"tokens-inexistente-{Guid.NewGuid():N}.json");
        var salida = new StringWriter();
        var error = new StringWriter();

        var codigo = Cli.Ejecutar(["tokens"], salida, error, ruta);

        Assert.Equal(1, codigo);
        Assert.Contains(ruta, error.ToString());
    }
}
