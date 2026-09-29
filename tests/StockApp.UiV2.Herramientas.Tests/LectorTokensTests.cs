using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.UiV2.Herramientas.Tests;

public class LectorTokensTests
{
    [Fact]
    public void Leer_JsonValido_DevuelveLasSeccionesEnOrdenDeDocumento()
    {
        var def = LectorTokens.Leer(JsonTokensDePrueba.Valido);

        Assert.Equal("V2", def.Prefijo);
        Assert.Equal(1.20996, def.FactorAltoLinea);
        Assert.Equal(
            new[] { ("Acento", "#1D4ED8"), ("Fondo", "#F7F7F5"), ("TextoSobreAcento", "#FFFFFF") },
            def.Colores.ToArray());
        Assert.Equal(new[] { ("Control", 8.0) }, def.Radios.ToArray());
        Assert.Equal(new[] { ("Control", 36.0) }, def.Alturas.ToArray());
        Assert.Equal(new[] { ("Sidebar", 240.0) }, def.Anchos.ToArray());
        Assert.Equal(new[] { 4.0, 8.0 }, def.Espaciado.ToArray());
        Assert.Equal(new SombraToken(0, 1, 2, 0, "#000000", 0.05), def.SombraCard);
        Assert.Equal(new EstiloTipografico(22, 600, 26, null, false), def.Tipografia[0].Estilo);
        Assert.Equal(new EstiloTipografico(11, 600, null, 0.6, true), def.Tipografia[1].Estilo);
    }

    [Fact]
    public void Leer_ColorEnMinusculas_LanzaNombrandoLaClave()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"#1D4ED8\"", "\"#1d4ed8\"");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("colores.Acento", ex.Message);
    }

    [Fact]
    public void Leer_EspaciadoNoMultiploDe4_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("[4, 8]", "[4, 6]");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("múltiplo de 4", ex.Message);
    }

    [Fact]
    public void Leer_PesoFueraDe400a700_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"peso\": 600, \"altoLinea\"", "\"peso\": 550, \"altoLinea\"");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("tipografia.TituloPagina.peso", ex.Message);
    }

    [Fact]
    public void Leer_NombreConGuion_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"Fondo\":", "\"Fondo-App\":");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("PascalCase", ex.Message);
    }

    [Fact]
    public void Leer_ColorRepetidoEntreColoresYDerivados_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"TextoSobreAcento\":", "\"Acento\":");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("duplicado 'Acento'", ex.Message);
    }

    [Fact]
    public void Leer_OpacidadMayorA1_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"opacidad\": 0.05", "\"opacidad\": 5");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("sombraCard.opacidad", ex.Message);
    }

    [Fact]
    public void Leer_SinSeccionAnchos_Lanza()
    {
        var json = JsonTokensDePrueba.Valido.Replace("\"anchos\": { \"Sidebar\": 240 },", "");

        var ex = Assert.Throws<TokensInvalidosException>(() => LectorTokens.Leer(json));

        Assert.Contains("'anchos'", ex.Message);
    }
}
