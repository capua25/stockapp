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
    /// vez de escapar. "tokens" es el primer comando que puede fallar así: sin tokens.json,
    /// LectorTokens.LeerArchivo dispara FileNotFoundException al leer el archivo.
    /// </summary>
    [Fact]
    public void Ejecutar_ComandoTokensSinArchivo_Devuelve1YElMensajeEnStderr()
    {
        var ruta = RutasRepo.TokensJson;
        var contenidoOriginal = File.ReadAllText(ruta);
        File.Delete(ruta);
        try
        {
            var salida = new StringWriter();
            var error = new StringWriter();

            var codigo = Cli.Ejecutar(["tokens"], salida, error);

            Assert.Equal(1, codigo);
            Assert.Contains(ruta, error.ToString());
        }
        finally
        {
            File.WriteAllText(ruta, contenidoOriginal);
        }
    }
}
