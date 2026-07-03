using System.Text.RegularExpressions;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Verifica que InfoApp expone la versión leída del assembly (InformationalVersion),
/// normalizando metadata de build y aplicando un fallback si el atributo no está disponible.
/// </summary>
public class InfoAppTests
{
    [Fact]
    public void Version_NoEsNulaNiVacia_YMatcheaPatronDeVersion()
    {
        var version = new InfoApp().Version;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Matches(new Regex(@"^\d+(\.\d+)+$"), version);
    }

    [Theory]
    [InlineData("0.1.1+abc", "0.1.1")]
    [InlineData(null, "0.0.0")]
    [InlineData("1.2.3", "1.2.3")]
    public void Normalizar_CasosEsperados(string? informational, string esperado)
    {
        var resultado = InfoApp.Normalizar(informational);

        Assert.Equal(esperado, resultado);
    }
}
