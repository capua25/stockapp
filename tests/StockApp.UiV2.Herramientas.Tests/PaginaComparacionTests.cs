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

    [Theory]
    [InlineData(0.5, 0, 0.5, 0, true)]
    [InlineData(0.51, 0, 0.5, 0, false)]
    [InlineData(0.1, 1, 0.5, 0, false)]
    public void Aprobada_ExigeAmbasCompuertas(double porcentaje, int teselas, double umbral, int maximo, bool esperado)
    {
        var distintos = (int)Math.Round(porcentaje * 10_000 / 100);
        Assert.Equal(esperado, PaginaComparacion.Aprobada(new ResultadoComparacion(100, 100, distintos, teselas), umbral, maximo));
    }
}
