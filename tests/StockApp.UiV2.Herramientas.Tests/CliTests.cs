using SkiaSharp;

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
    /// vez de escapar. "tokens" es el primer comando que puede fallar así: con una ruta de
    /// tokens.json inexistente, LectorTokens.LeerArchivo dispara FileNotFoundException al leer
    /// el archivo. Usa el parámetro rutaTokensJson (solo para tests) para no tocar el
    /// docs/ui-v2/tokens.json real: ese archivo está trackeado en git y ningún test debe
    /// borrarlo ni escribirlo, ni siquiera transitoriamente.
    /// </summary>
    [Fact]
    public void Ejecutar_ComandoTokensSinArchivo_Devuelve1YElMensajeEnStderr()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"tokens-inexistente-{Guid.NewGuid():N}.json");
        var salida = new StringWriter();
        var error = new StringWriter();

        var codigo = Cli.Ejecutar(["tokens"], salida, error, ruta);

        Assert.Equal(1, codigo);
        Assert.Contains(ruta, error.ToString());
    }

    /// <summary>
    /// Cierra el minor diferido de la Task 4: el catch de Ejecutar tampoco tenía un test propio
    /// para "icono-svg" con una clave que el proveedor de íconos no conoce. ExportadorIconos.Svg
    /// convierte el KeyNotFoundException del proveedor en ArgumentException (ver comentario en
    /// ExportadorIconos.cs) justamente para que Cli.Ejecutar lo capture y salga limpio con 1.
    /// </summary>
    [Fact]
    public void Ejecutar_IconoSvgClaveInexistente_Devuelve1YElMensajeEnStderr()
    {
        var salida = new StringWriter();
        var error = new StringWriter();

        var codigo = Cli.Ejecutar(["icono-svg", "mdi-no-existe-este-icono"], salida, error);

        Assert.Equal(1, codigo);
        Assert.Contains("mdi-no-existe-este-icono", error.ToString());
    }

    /// <summary>
    /// Decisión del controlador para la Task 5: comparar-png también puede fallar (archivo
    /// inexistente, tamaños distintos, imagen uniforme) y esos casos deben salir por el mismo
    /// catch de Ejecutar (InvalidOperationException), no como stack trace sin manejar.
    /// ComparadorImagenes.Decodificar delega en SKBitmap.Decode, que devuelve null (no lanza)
    /// ante un archivo inexistente, y Decodificar lo traduce a InvalidOperationException.
    /// Usa un directorio temporal: ningún test debe escribir en el repo.
    /// </summary>
    [Fact]
    public void Ejecutar_CompararPngArchivoInexistente_Devuelve1YElMensajeEnStderr()
    {
        var carpeta = Directory.CreateTempSubdirectory("cli-comparar-png-").FullName;
        var candidata = PngDePrueba.Crear(carpeta, "candidata.png", 10, 10, SKColors.White);
        var referenciaInexistente = Path.Combine(carpeta, "no-existe.png");
        var diff = Path.Combine(carpeta, "diff.png");
        var salida = new StringWriter();
        var error = new StringWriter();

        var codigo = Cli.Ejecutar(["comparar-png", referenciaInexistente, candidata, diff], salida, error);

        Assert.Equal(1, codigo);
        Assert.Contains(referenciaInexistente, error.ToString());
    }

    /// <summary>
    /// Task 5b: "capturar-mockup ... --tamano 1366x768" captura al tamaño del municipio. El HTML
    /// se escribe en una carpeta temporal (ningún test escribe en el repo).
    /// </summary>
    [Fact]
    public void Ejecutar_CapturarMockupConTamano1366x768_EscribeUnPngDeEseTamano()
    {
        var carpeta = Directory.CreateTempSubdirectory("cli-capturar-").FullName;
        var html = Path.Combine(carpeta, "m.html");
        File.WriteAllText(html,
            "<!doctype html><html><head><meta charset=\"utf-8\">"
            + $"<link rel=\"stylesheet\" href=\"{new Uri(RutasRepo.TokensCss).AbsoluteUri}\">"
            + $"<link rel=\"stylesheet\" href=\"{new Uri(Path.Combine(RutasRepo.CarpetaBaseMockups, "base.css")).AbsoluteUri}\">"
            + "</head><body><div class=\"v2-ventana\"></div></body></html>");
        var png = Path.Combine(carpeta, "m.png");
        var error = new StringWriter();

        var codigo = Cli.Ejecutar(["capturar-mockup", html, png, "--tamano", "1366x768"], new StringWriter(), error);

        Assert.True(codigo == 0, error.ToString());
        using var imagen = SKBitmap.Decode(png);
        Assert.Equal(1366, imagen.Width);
        Assert.Equal(768, imagen.Height);
    }

    [Fact]
    public void Ejecutar_CapturarMockupConTamanoDesconocido_Devuelve2YListaLosValidos()
    {
        var error = new StringWriter();

        var codigo = Cli.Ejecutar(["capturar-mockup", "a.html", "a.png", "--tamano", "1920x1080"], new StringWriter(), error);

        Assert.Equal(2, codigo);
        Assert.Contains("1920x1080", error.ToString());
        Assert.Contains("1280x760", error.ToString());
        Assert.Contains("1366x768", error.ToString());
    }
}
