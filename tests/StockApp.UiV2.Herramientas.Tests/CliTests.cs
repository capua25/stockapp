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
}
