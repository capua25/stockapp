using System.Text.Json;
using StockApp.UiV2.Herramientas.Imagenes;

namespace StockApp.UiV2.Herramientas.Tests;

public class PaginaComparacionTests
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("comparacion-").FullName;

    private string Pantalla(string nombre) => Directory.CreateDirectory(Path.Combine(_raiz, nombre)).FullName;

    [Fact]
    public void Escribir_DentroDeAmbosUmbrales_MuestraLasTresImagenesYElVeredicto()
    {
        var carpeta = Pantalla("componentes-catalogo");

        var pagina = PaginaComparacion.Escribir(carpeta, "componentes-catalogo", new ResultadoComparacion(1280, 760, 97, 0), 0.5, 0);

        var html = File.ReadAllText(pagina);
        Assert.Contains("src=\"mockup.png\"", html);
        Assert.Contains("src=\"app.png\"", html);
        Assert.Contains("src=\"diff.png\"", html);
        Assert.Contains("0.010%", html);
        Assert.Contains("APROBADA", html);
    }

    [Fact]
    public void Escribir_ConTeselasDensasPorEncimaDelMaximo_EsRechazadaAunqueElPorcentajeSeaBajo()
    {
        var carpeta = Pantalla("x");

        var html = File.ReadAllText(PaginaComparacion.Escribir(carpeta, "x", new ResultadoComparacion(1280, 760, 1600, 14), 0.5, 0));

        Assert.Contains("RECHAZADA", html);
        Assert.Contains("Teselas densas: 14 (máximo 0)", html);
    }

    [Fact]
    public void Escribir_PersisteResultadoJson()
    {
        var carpeta = Pantalla("x");

        PaginaComparacion.Escribir(carpeta, "x", new ResultadoComparacion(100, 100, 5, 1), 1.0, 2);

        var persistido = JsonSerializer.Deserialize<PaginaComparacion.ResultadoPersistido>(
            File.ReadAllText(Path.Combine(carpeta, "resultado.json")))!;
        Assert.Equal(new PaginaComparacion.ResultadoPersistido("x", 5, 0.05, 1, 1.0, 2), persistido);
    }

    [Fact]
    public void Escribir_RegeneraElIndiceConTodasLasPantallasOrdenadas()
    {
        PaginaComparacion.Escribir(Pantalla("b-pantalla"), "b-pantalla", new ResultadoComparacion(10, 10, 0, 0), 1, 0);
        PaginaComparacion.Escribir(Pantalla("a-pantalla"), "a-pantalla", new ResultadoComparacion(10, 10, 50, 3), 1, 0);

        var indice = File.ReadAllText(Path.Combine(_raiz, "index.html"));

        Assert.True(indice.IndexOf("a-pantalla/index.html", StringComparison.Ordinal)
                    < indice.IndexOf("b-pantalla/index.html", StringComparison.Ordinal));
        Assert.Contains("RECHAZADA", indice);
        Assert.Contains("APROBADA", indice);
    }

    [Fact]
    public void EscribirIndice_ConCaracteresEspecialesEnElNombreDePantalla_CodificaElHrefComoSegmentoDeUrl()
    {
        var nombre = "pantalla#100%raro";

        PaginaComparacion.Escribir(Pantalla(nombre), nombre, new ResultadoComparacion(10, 10, 0, 0), 1, 0);

        var indice = File.ReadAllText(Path.Combine(_raiz, "index.html"));

        Assert.Contains("href=\"pantalla%23100%25raro/index.html\"", indice);
    }

    [Theory]
    [InlineData(0.5, 0, 0.5, 0, true)]
    [InlineData(0.51, 0, 0.5, 0, false)]
    [InlineData(0.1, 1, 0.5, 0, false)]
    public void Aprobada_ExigeAmbasCompuertas(double porcentaje, int teselas, double umbral, int maximo, bool esperado)
    {
        var distintos = (int)Math.Round(porcentaje * 10_000 / 100);
        Assert.Equal(esperado, PaginaComparacion.Aprobada(new ResultadoComparacion(100, 100, distintos, teselas), umbral, maximo));
    }

    /// <summary>
    /// I2 (revisión final): los píxeles no ven un glifo recortado (spec §6.5) — el guardián de
    /// texto es aparte. Antes de este fix, Escribir/Aprobada solo miraban píxeles, así que una
    /// pantalla con texto recortado pero píxeles dentro del umbral quedaba persistida como
    /// APROBADA en resultado.json, la página y el índice, aunque el test terminara en rojo por el
    /// Assert del guardián (ComparacionVisual.cs:50).
    /// </summary>
    [Fact]
    public void Aprobada_ConRecortes_EsFalsaAunqueLosPixelesEstenBien() =>
        Assert.False(PaginaComparacion.Aprobada(
            new ResultadoComparacion(100, 100, 0, 0), umbralPorcentaje: 1.0, teselasDensasMax: 2,
            recortes: ["Texto recortado"]));

    [Fact]
    public void Escribir_ConRecortes_MuestraRechazadaYListaLosRecortes()
    {
        var carpeta = Pantalla("con-recorte");

        var html = File.ReadAllText(PaginaComparacion.Escribir(
            carpeta, "con-recorte", new ResultadoComparacion(100, 100, 0, 0), 1.0, 2,
            recortes: ["Texto recortado a propósito"]));

        Assert.Contains("RECHAZADA", html);
        Assert.DoesNotContain("APROBADA", html);
        Assert.Contains(System.Net.WebUtility.HtmlEncode("Texto recortado a propósito"), html);
    }

    [Fact]
    public void Escribir_ConRecortes_PersisteLosRecortesEnElJson()
    {
        var carpeta = Pantalla("con-recorte-json");

        PaginaComparacion.Escribir(
            carpeta, "con-recorte-json", new ResultadoComparacion(100, 100, 0, 0), 1.0, 2,
            recortes: ["Texto recortado a propósito"]);

        var persistido = JsonSerializer.Deserialize<PaginaComparacion.ResultadoPersistido>(
            File.ReadAllText(Path.Combine(carpeta, "resultado.json")))!;
        Assert.Equal(["Texto recortado a propósito"], persistido.Recortes);
    }

    [Fact]
    public void Escribir_ConRecortes_ElIndiceTambienDiceRechazada()
    {
        PaginaComparacion.Escribir(
            Pantalla("con-recorte-indice"), "con-recorte-indice", new ResultadoComparacion(100, 100, 0, 0), 1.0, 2,
            recortes: ["Texto recortado a propósito"]);

        var indice = File.ReadAllText(Path.Combine(_raiz, "index.html"));

        Assert.Contains("RECHAZADA", indice);
    }

    /// <summary>
    /// I2b (revisión final): si CapturadorMockup.CapturarAsync lanza (mockup borrado, fuente sin
    /// cargar, etc.), ComparacionVisual nunca llega a llamar a Escribir — pero el resultado.json
    /// de la corrida anterior (aprobada) seguía ahí, y EscribirIndice lo listaba como vigente.
    /// BorrarResultadoAnterior es lo que ComparacionVisual llama ANTES de intentar capturar.
    /// </summary>
    [Fact]
    public void BorrarResultadoAnterior_QuitaLaPantallaDelIndice()
    {
        var carpeta = Pantalla("transitoria");
        PaginaComparacion.Escribir(carpeta, "transitoria", new ResultadoComparacion(100, 100, 0, 0), 1.0, 2);
        Assert.True(File.Exists(Path.Combine(carpeta, PaginaComparacion.ArchivoResultado)));
        Assert.Contains("transitoria", File.ReadAllText(Path.Combine(_raiz, "index.html")));

        PaginaComparacion.BorrarResultadoAnterior(carpeta);

        Assert.False(File.Exists(Path.Combine(carpeta, PaginaComparacion.ArchivoResultado)));
        Assert.False(File.Exists(Path.Combine(carpeta, "index.html")));
        Assert.DoesNotContain("transitoria", File.ReadAllText(Path.Combine(_raiz, "index.html")));
    }

    [Fact]
    public void BorrarResultadoAnterior_SinCorridaPrevia_NoFalla()
    {
        var carpeta = Pantalla("nunca-corrio");

        PaginaComparacion.BorrarResultadoAnterior(carpeta);

        Assert.False(File.Exists(Path.Combine(carpeta, PaginaComparacion.ArchivoResultado)));
    }

    /// <summary>
    /// Task 5b: el índice agrupa por pantalla y tamaño. El orden ordinal de las carpetas pondría
    /// "acceso-login-error" entre "acceso-login" y "acceso-login@1366x768"; agrupado, los dos
    /// tamaños de una pantalla quedan juntos, primero el estándar.
    /// </summary>
    [Fact]
    public void Indice_AgrupaCadaPantallaConSusDosTamanos()
    {
        var r = new ResultadoComparacion(1280, 760, 0, 0);
        foreach (var nombre in new[] { "acceso-login@1366x768", "acceso-login-error", "acceso-login" })
        {
            PaginaComparacion.Escribir(Pantalla(nombre), nombre, r, 0.5, 0);
        }

        var indice = File.ReadAllText(Path.Combine(_raiz, "index.html"));

        Assert.Contains("<th>Tamaño</th>", indice);
        var login = indice.IndexOf("\">acceso-login</a></td><td>1280x760</td>", StringComparison.Ordinal);
        var login1366 = indice.IndexOf("\">acceso-login</a></td><td>1366x768</td>", StringComparison.Ordinal);
        var error = indice.IndexOf("\">acceso-login-error</a></td><td>1280x760</td>", StringComparison.Ordinal);
        Assert.True(login >= 0 && login1366 >= 0 && error >= 0, indice);
        Assert.True(login < login1366 && login1366 < error, indice);
    }
}
