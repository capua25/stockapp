using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// Fija los valores de docs/ui-v2/tokens.json a los de la spec (§2.1 a §2.3). tokens.json es la
/// única fuente: si alguien "ajusta" un color para que una pantalla cierre con su mockup, este
/// test lo frena. Cambiar un valor acá exige cambiar primero la spec.
/// </summary>
public class TokensSpecTests
{
    private static readonly DefinicionTokens Tokens = LectorTokens.LeerArchivo(RutasRepo.TokensJson);

    private static string Color(string nombre) => Tokens.Colores.Single(c => c.Nombre == nombre).Hex;

    [Fact]
    public void Prefijo_EsV2MientrasConvivenConLosTokensViejos() => Assert.Equal("V2", Tokens.Prefijo);

    [Fact]
    public void FactorAltoLinea_EsElDeLaInterEmbebida() => Assert.Equal(1.20996, Tokens.FactorAltoLinea);

    [Theory]
    [InlineData("Fondo", "#F7F7F5")]
    [InlineData("Superficie", "#FFFFFF")]
    [InlineData("Borde", "#E7E5E4")]
    [InlineData("BordeInput", "#D6D3D1")]
    [InlineData("SeparadorGrilla", "#F0EFED")]
    [InlineData("HeaderGrilla", "#FAFAF9")]
    [InlineData("Acento", "#1D4ED8")]
    [InlineData("AcentoHover", "#1E40AF")]
    [InlineData("AcentoSuave", "#DBEAFE")]
    [InlineData("AcentoOscuro", "#1E3A8A")]
    [InlineData("TextoPrimario", "#1C1917")]
    [InlineData("TextoSecundario", "#57534E")]
    [InlineData("TextoTerciario", "#78716C")]
    [InlineData("TextoEtiqueta", "#44403C")]
    [InlineData("TextoGrupo", "#A8A29E")]
    [InlineData("PeligroTexto", "#B91C1C")]
    [InlineData("BordeError", "#DC2626")]
    [InlineData("InsigniaNeutroFondo", "#F5F5F4")]
    [InlineData("InsigniaNeutroTexto", "#44403C")]
    [InlineData("InsigniaAdvertenciaFondo", "#FEF3C7")]
    [InlineData("InsigniaAdvertenciaTexto", "#92400E")]
    [InlineData("InsigniaPeligroFondo", "#FEE2E2")]
    [InlineData("InsigniaPeligroTexto", "#991B1B")]
    [InlineData("InsigniaExitoFondo", "#DCFCE7")]
    [InlineData("InsigniaExitoTexto", "#14532D")]
    [InlineData("AlertaAdvertenciaFondo", "#FEF3C7")]
    [InlineData("AlertaAdvertenciaBorde", "#FDE68A")]
    [InlineData("AlertaAdvertenciaTexto", "#92400E")]
    [InlineData("AlertaErrorFondo", "#FEE2E2")]
    [InlineData("AlertaErrorBorde", "#FCA5A5")]
    [InlineData("AlertaErrorTexto", "#991B1B")]
    [InlineData("AlertaInfoFondo", "#DBEAFE")]
    [InlineData("AlertaInfoBorde", "#DBEAFE")]
    [InlineData("AlertaInfoTexto", "#1E3A8A")]
    public void Color_TieneElValorDeLaSpec(string nombre, string hex) => Assert.Equal(hex, Color(nombre));

    /// <summary>
    /// Derivados de Fase 0: la spec no los nombra pero los necesitan los estados de los
    /// controles. Ninguno introduce un hex nuevo: todos repiten un valor ya aprobado.
    /// </summary>
    [Theory]
    [InlineData("TextoSobreAcento", "Superficie")]
    [InlineData("HoverNeutro", "InsigniaNeutroFondo")]
    [InlineData("DeshabilitadoFondo", "InsigniaNeutroFondo")]
    [InlineData("DeshabilitadoTexto", "TextoGrupo")]
    public void ColorDerivado_RepiteUnValorAprobado(string derivado, string origen) =>
        Assert.Equal(Color(origen), Color(derivado));

    [Theory]
    [InlineData("Control", 8)]
    [InlineData("Card", 10)]
    [InlineData("Insignia", 999)]
    [InlineData("Casilla", 4)]
    public void Radio_TieneElValorDeLaSpec(string nombre, double valor) =>
        Assert.Equal(valor, Tokens.Radios.Single(r => r.Nombre == nombre).Valor);

    [Theory]
    [InlineData("Control", 36)]
    [InlineData("FilaGrilla", 40)]
    [InlineData("HeaderGrilla", 36)]
    [InlineData("ItemSidebar", 32)]
    public void Altura_TieneElValorDeLaSpec(string nombre, double valor) =>
        Assert.Equal(valor, Tokens.Alturas.Single(a => a.Nombre == nombre).Valor);

    [Theory]
    [InlineData("Sidebar", 240)]
    [InlineData("FormularioMax", 760)]
    [InlineData("CardLogin", 400)]
    public void Ancho_TieneElValorDeLaSpec(string nombre, double valor) =>
        Assert.Equal(valor, Tokens.Anchos.Single(a => a.Nombre == nombre).Valor);

    [Fact]
    public void Sombra_EsLaUnicaDeLaSpec() =>
        Assert.Equal(new SombraToken(0, 1, 2, 0, "#000000", 0.05), Tokens.SombraCard);

    [Theory]
    [InlineData("TituloPagina", 22, 600)]
    [InlineData("TituloSeccion", 15, 600)]
    [InlineData("Marca", 15, 700)]
    [InlineData("ValorMetrica", 20, 600)]
    [InlineData("TituloLogin", 20, 600)]
    [InlineData("Cuerpo", 14, 400)]
    [InlineData("Control", 13, 400)]
    [InlineData("SubtituloFormulario", 13, 600)]
    [InlineData("EtiquetaCampo", 12, 500)]
    [InlineData("ItemSidebar", 12, 500)]
    [InlineData("Insignia", 12, 500)]
    [InlineData("ErrorCampo", 12, 400)]
    [InlineData("HeaderColumna", 11, 600)]
    [InlineData("HeaderSeccionSidebar", 11, 600)]
    public void Tipografia_TieneTamanoYPesoDeLaSpec(string rol, double tamano, int peso)
    {
        var estilo = Tokens.Tipografia.Single(t => t.Nombre == rol).Estilo;
        Assert.Equal(tamano, estilo.Tamano);
        Assert.Equal(peso, estilo.Peso);
    }

    [Fact]
    public void TituloPagina_TieneAltoDeLinea26() =>
        Assert.Equal(26, Tokens.Tipografia.Single(t => t.Nombre == "TituloPagina").Estilo.AltoLinea);

    [Theory]
    [InlineData("HeaderColumna")]
    [InlineData("HeaderSeccionSidebar")]
    public void Headers_VanEnMayusculasConEspaciado06(string rol)
    {
        var estilo = Tokens.Tipografia.Single(t => t.Nombre == rol).Estilo;
        Assert.True(estilo.Mayusculas);
        Assert.Equal(0.6, estilo.EspaciadoLetras);
    }
}
