using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// Los archivos generados se commitean (el build de Avalonia y los mockups los necesitan sin
/// correr nada). Este test es la red contra la "segunda copia a mano" que la spec §3.2 prohíbe:
/// si alguien edita Tokens.axaml o tokens.css directamente, o cambia tokens.json sin regenerar,
/// falla acá.
/// </summary>
public class ArchivosGeneradosSincronizadosTests
{
    private static readonly DefinicionTokens Tokens = LectorTokens.LeerArchivo(RutasRepo.TokensJson);

    [Fact]
    public void TokensAxaml_CoincideConLoQueGeneraTokensJson() =>
        Assert.Equal(GeneradorTokens.GenerarAxaml(Tokens), Cli.Normalizar(File.ReadAllText(RutasRepo.TokensAxaml)));

    [Fact]
    public void TokensCss_CoincideConLoQueGeneraTokensJson() =>
        Assert.Equal(GeneradorTokens.GenerarCss(Tokens), Cli.Normalizar(File.ReadAllText(RutasRepo.TokensCss)));

    [Fact]
    public void ComandoVerificar_DevuelveCero()
    {
        var salida = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, Cli.Ejecutar(["tokens", "--verificar"], salida, error));
        Assert.Contains("al día", salida.ToString());
    }

    [Fact]
    public void Normalizar_AceptaFinDeLineaDeWindows() =>
        Assert.Equal("a\nb\n", Cli.Normalizar("a\r\nb\r\n"));
}
