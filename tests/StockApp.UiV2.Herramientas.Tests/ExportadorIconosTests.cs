using StockApp.UiV2.Herramientas.Mockups;

namespace StockApp.UiV2.Herramientas.Tests;

public class ExportadorIconosTests
{
    [Fact]
    public void Svg_IconoExistente_UsaElPathYElViewBoxDelProveedorDeLaApp()
    {
        var svg = ExportadorIconos.Svg("mdi-package-variant");

        Assert.StartsWith("<svg class=\"v2-icono\" width=\"16\" height=\"16\" viewBox=\"0 0 24 24\"", svg);
        Assert.Contains("<path fill=\"currentColor\" d=\"M2,10.96C1.5,10.68", svg);
    }

    [Fact]
    public void Svg_IconoInexistente_Lanza() =>
        Assert.ThrowsAny<Exception>(() => ExportadorIconos.Svg("mdi-no-existe-este-icono"));

    [Fact]
    public void Cli_IconoSvg_ImprimeElSvg()
    {
        var salida = new StringWriter();

        Assert.Equal(0, Cli.Ejecutar(["icono-svg", "mdi-package-variant"], salida, new StringWriter()));
        Assert.StartsWith("<svg", salida.ToString());
    }
}
