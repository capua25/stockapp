using System.Globalization;
using StockApp.Domain.Formato;

namespace StockApp.Domain.Tests.Formato;

/// <summary>
/// Punto de verdad del formato numérico de la app (decisión 2026-10-01: es-UY en TODA la app,
/// coma decimal y punto de miles, no configurable). Cubre formateo, la tabla de parseo seguro y
/// la independencia de la cultura del hilo/SO.
/// </summary>
public class FormatoEsUyTests
{
    // ── Formateo ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("26400", "$ 26.400,00")]
    [InlineData("0", "$ 0,00")]
    [InlineData("-600", "-$ 600,00")]
    [InlineData("850.5000", "$ 850,50")]
    [InlineData("1234567.891", "$ 1.234.567,89")]
    public void Moneda_FormateaConSimboloMilesConPuntoYDosDecimalesConComa(string valor, string esperado)
    {
        Assert.Equal(esperado, FormatoEsUy.Moneda(decimal.Parse(valor, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("22", "22")]
    [InlineData("22.0000", "22")]
    [InlineData("22.5", "22,5")]
    [InlineData("-6", "-6")]
    [InlineData("1500", "1.500")]
    [InlineData("1500.25", "1.500,25")]
    [InlineData("0.5", "0,5")]
    public void Cantidad_SinCerosDeRellenoConMilesYComaDecimal(string valor, string esperado)
    {
        Assert.Equal(esperado, FormatoEsUy.Cantidad(decimal.Parse(valor, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("26400", "26.400,00")]
    [InlineData("5.4", "5,40")]
    [InlineData("-3.25", "-3,25")]
    public void Decimal_DosDecimalesFijosConMilesSinSimbolo(string valor, string esperado)
    {
        Assert.Equal(esperado, FormatoEsUy.Decimal(decimal.Parse(valor, CultureInfo.InvariantCulture)));
    }

    // ── Tabla de parseo ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("1500", "1500")]
    [InlineData("1500,5", "1500.5")]
    [InlineData("1.500", "1500")]
    [InlineData("1.500,50", "1500.50")]
    [InlineData("-3,25", "-3.25")]
    [InlineData("0,5", "0.5")]
    [InlineData("5,4", "5.4")]
    [InlineData("1.234.567,89", "1234567.89")]
    [InlineData("  12,35  ", "12.35")]
    [InlineData("0", "0")]
    public void TryParseDecimal_Acepta(string texto, string esperado)
    {
        var ok = FormatoEsUy.TryParseDecimal(texto, out var valor, out var error);

        Assert.True(ok, $"'{texto}' debía aceptarse; error: {error}");
        Assert.Equal(decimal.Parse(esperado, CultureInfo.InvariantCulture), valor);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("5.4")]
    [InlineData("1.5")]
    [InlineData("12.34")]
    [InlineData("1.50,5")]
    [InlineData("1,500.50")]
    [InlineData("1.5000")]
    [InlineData("1500.5")]
    [InlineData("1.500.5")]
    [InlineData(".5")]
    [InlineData("5,")]
    [InlineData("1,2,3")]
    [InlineData("abc")]
    [InlineData("1 500")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseDecimal_Rechaza_ConMensaje(string? texto)
    {
        var ok = FormatoEsUy.TryParseDecimal(texto, out var valor, out var error);

        Assert.False(ok, $"'{texto}' debía rechazarse pero dio {valor}");
        Assert.Equal(0m, valor);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("5.4", "Usá coma para los decimales: 5,4")]
    [InlineData("1.5", "Usá coma para los decimales: 1,5")]
    [InlineData("12.34", "Usá coma para los decimales: 12,34")]
    [InlineData("-3.25", "Usá coma para los decimales: -3,25")]
    [InlineData("1,500.50", "Usá coma para los decimales y punto para los miles: 1.500,50")]
    public void TryParseDecimal_PuntoComoDecimal_SugiereLaFormaCorrecta(string texto, string mensaje)
    {
        FormatoEsUy.TryParseDecimal(texto, out _, out var error);

        Assert.Equal(mensaje, error);
    }

    [Fact]
    public void TryParseDecimal_AgrupacionInvalidaSinSugerenciaObvia_ExplicaLaRegla()
    {
        FormatoEsUy.TryParseDecimal("1.50,5", out _, out var error);

        Assert.Equal(FormatoEsUy.MensajeAgrupacionInvalida, error);
    }

    [Fact]
    public void TryParseDecimal_Texto_MensajeGenerico()
    {
        FormatoEsUy.TryParseDecimal("abc", out _, out var error);

        Assert.Equal(FormatoEsUy.MensajeNoEsNumero, error);
    }

    [Fact]
    public void RoundTrip_CantidadYDecimal_SeVuelvenAParsearAlMismoValor()
    {
        foreach (var valor in new[] { 1500.5m, -3.25m, 0.5m, 1234567.89m, 27m })
        {
            Assert.True(FormatoEsUy.TryParseDecimal(FormatoEsUy.Cantidad(valor), out var c, out _));
            Assert.Equal(valor, c);
            Assert.True(FormatoEsUy.TryParseDecimal(FormatoEsUy.Decimal(valor), out var d, out _));
            Assert.Equal(valor, d);
        }
    }

    // ── Independencia de la cultura del hilo / SO (guardián) ────────────────

    public static TheoryData<string> CulturasHostiles => new() { "en-US", "" /* Invariant */, "de-DE", "es-UY" };

    [Theory]
    [MemberData(nameof(CulturasHostiles))]
    public void SalidaYParseo_NoDependenDeLaCulturaDelHilo(string nombreCultura)
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            var cultura = nombreCultura.Length == 0
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(nombreCultura);
            CultureInfo.CurrentCulture = cultura;
            CultureInfo.CurrentUICulture = cultura;

            Assert.Equal("$ 26.400,00", FormatoEsUy.Moneda(26400m));
            Assert.Equal("1.500,5", FormatoEsUy.Cantidad(1500.5m));
            Assert.Equal("26.400,00", FormatoEsUy.Decimal(26400m));
            Assert.True(FormatoEsUy.TryParseDecimal("1.500,50", out var v, out _));
            Assert.Equal(1500.50m, v);
            Assert.False(FormatoEsUy.TryParseDecimal("5.4", out _, out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    // ── Cultura de la UI construida desde el mismo punto de verdad ──────────

    [Fact]
    public void CrearCultura_FormateaIgualQueElPuntoDeVerdad()
    {
        var cultura = FormatoEsUy.CrearCultura();

        Assert.Equal("26.400,00", 26400m.ToString("N2", cultura));
        Assert.Equal("$ 26.400,00", 26400m.ToString("C2", cultura));
        Assert.Equal("1.234", 1234.ToString("N0", cultura));
        Assert.Equal("5,4", 5.4m.ToString(cultura));
    }

    [Fact]
    public void CrearCultura_DevuelveInstanciasIndependientes_NoSePuedeCorromperElPuntoDeVerdad()
    {
        var cultura = FormatoEsUy.CrearCultura();
        cultura.NumberFormat.NumberDecimalSeparator = "#";

        Assert.Equal("26.400,00", FormatoEsUy.Decimal(26400m));
        Assert.Equal("26.400,00", 26400m.ToString("N2", FormatoEsUy.CrearCultura()));
    }

    [Fact]
    public void Formato_EsDeSoloLectura()
    {
        Assert.True(FormatoEsUy.Formato.IsReadOnly);
    }

    /// <summary>El formato se arma a mano (no depende de ICU). Cuando ICU sí tiene es-UY, la
    /// salida tiene que coincidir con ella: el fallback manual no puede divergir de la cultura real.</summary>
    [Fact]
    public void Formato_CoincideConEsUyDeIcu_CuandoEstaDisponible()
    {
        CultureInfo icu;
        try { icu = CultureInfo.GetCultureInfo("es-UY"); }
        catch (CultureNotFoundException) { return; }

        foreach (var valor in new[] { 26400m, -600m, 0.5m, 1234567.891m, -3.25m })
        {
            Assert.Equal(valor.ToString("C2", icu), FormatoEsUy.Moneda(valor));
            Assert.Equal(valor.ToString("N2", icu), FormatoEsUy.Decimal(valor));
        }
    }
}
