using SkiaSharp;

namespace StockApp.UiV2.Herramientas.Imagenes;

/// <summary>
/// Compara dos PNG del mismo tamaño. Un píxel es distinto si algún canal RGB difiere en más de
/// la tolerancia (absorbe el antialiasing de texto entre Chromium y Skia). Además de contar
/// píxeles, agrupa en teselas de 16x16: un defecto real concentra diferencias (algo corrido:
/// muchas fuertes; un relleno de otro color pálido: casi todas leves) y el antialiasing las
/// reparte finas por los bordes de los glifos.
/// La imagen de diferencias pinta la referencia aclarada en gris, las fuertes en rojo, las leves
/// en naranja y el contorno de cada tesela densa en magenta.
/// </summary>
public static class ComparadorImagenes
{
    public const int ToleranciaCanalPorDefecto = 32;
    public const int TamanoTesela = 16;
    public const double DensidadTeselaMinima = 0.25;
    public const int ToleranciaColor = 4;
    public const double DensidadColorMinima = 0.9;

    private static readonly SKColor Rojo = new(255, 0, 0);
    private static readonly SKColor Naranja = new(255, 153, 0);
    private static readonly SKColor Magenta = new(255, 0, 255);

    public static ResultadoComparacion Comparar(
        string rutaReferencia, string rutaCandidata, string rutaDiff, int toleranciaCanal = ToleranciaCanalPorDefecto)
    {
        using var referencia = Decodificar(rutaReferencia);
        using var candidata = Decodificar(rutaCandidata);
        if (referencia.Width != candidata.Width || referencia.Height != candidata.Height)
        {
            throw new ArgumentException(
                $"Tamaños distintos: {Path.GetFileName(rutaReferencia)} es {referencia.Width}x{referencia.Height} "
                + $"y {Path.GetFileName(rutaCandidata)} es {candidata.Width}x{candidata.Height}.");
        }

        ValidarNoUniforme(referencia, rutaReferencia);
        ValidarNoUniforme(candidata, rutaCandidata);

        var ancho = referencia.Width;
        var alto = referencia.Height;
        var distinto = new bool[ancho * alto];
        var distintoLeve = new bool[ancho * alto];
        var total = 0;
        using var diff = new SKBitmap(ancho, alto, SKColorType.Rgba8888, SKAlphaType.Opaque);

        for (var y = 0; y < alto; y++)
        {
            for (var x = 0; x < ancho; x++)
            {
                var a = referencia.GetPixel(x, y);
                var b = candidata.GetPixel(x, y);
                var delta = Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
                if (delta > ToleranciaColor)
                {
                    distintoLeve[(y * ancho) + x] = true;
                }

                if (delta > toleranciaCanal)
                {
                    distinto[(y * ancho) + x] = true;
                    total++;
                    diff.SetPixel(x, y, Rojo);
                }
                else if (delta > ToleranciaColor)
                {
                    diff.SetPixel(x, y, Naranja);
                }
                else
                {
                    var gris = (byte)(191 + (Luminancia(a) / 4));
                    diff.SetPixel(x, y, new SKColor(gris, gris, gris));
                }
            }
        }

        var teselasDensas = 0;
        for (var ty = 0; ty < alto; ty += TamanoTesela)
        {
            for (var tx = 0; tx < ancho; tx += TamanoTesela)
            {
                var w = Math.Min(TamanoTesela, ancho - tx);
                var h = Math.Min(TamanoTesela, alto - ty);
                var fuertes = 0;
                var leves = 0;
                for (var y = ty; y < ty + h; y++)
                {
                    for (var x = tx; x < tx + w; x++)
                    {
                        if (distinto[(y * ancho) + x])
                        {
                            fuertes++;
                        }

                        if (distintoLeve[(y * ancho) + x])
                        {
                            leves++;
                        }
                    }
                }

                if (fuertes >= DensidadTeselaMinima * w * h || leves >= DensidadColorMinima * w * h)
                {
                    teselasDensas++;
                    MarcarContorno(diff, tx, ty, w, h);
                }
            }
        }

        Guardar(diff, rutaDiff);
        return new ResultadoComparacion(ancho, alto, total, teselasDensas);
    }

    private static SKBitmap Decodificar(string ruta) =>
        SKBitmap.Decode(ruta) ?? throw new InvalidOperationException($"No se pudo leer el PNG {ruta}.");

    private static void ValidarNoUniforme(SKBitmap imagen, string ruta)
    {
        var primero = imagen.GetPixel(0, 0);
        for (var y = 0; y < imagen.Height; y++)
        {
            for (var x = 0; x < imagen.Width; x++)
            {
                if (imagen.GetPixel(x, y) != primero)
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException(
            $"{Path.GetFileName(ruta)} es uniforme (un solo color): la captura salió vacía.");
    }

    private static int Luminancia(SKColor c) => ((c.Red * 299) + (c.Green * 587) + (c.Blue * 114)) / 1000;

    private static void MarcarContorno(SKBitmap diff, int x0, int y0, int w, int h)
    {
        for (var x = x0; x < x0 + w; x++)
        {
            diff.SetPixel(x, y0, Magenta);
            diff.SetPixel(x, y0 + h - 1, Magenta);
        }

        for (var y = y0; y < y0 + h; y++)
        {
            diff.SetPixel(x0, y, Magenta);
            diff.SetPixel(x0 + w - 1, y, Magenta);
        }
    }

    private static void Guardar(SKBitmap bitmap, string ruta)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        using var imagen = SKImage.FromBitmap(bitmap);
        using var datos = imagen.Encode(SKEncodedImageFormat.Png, 100);
        using var archivo = File.Create(ruta);
        datos.SaveTo(archivo);
    }
}
