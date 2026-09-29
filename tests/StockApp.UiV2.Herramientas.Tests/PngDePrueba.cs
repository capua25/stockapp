using SkiaSharp;

namespace StockApp.UiV2.Herramientas.Tests;

internal static class PngDePrueba
{
    /// <summary>
    /// PNG de ancho x alto con fondo liso, un píxel negro fijo en (0,0) para que nunca sea
    /// uniforme, y los rectángulos pedidos.
    /// </summary>
    public static string Crear(string carpeta, string nombre, int ancho, int alto, SKColor fondo,
        params (int X, int Y, int Ancho, int Alto, SKColor Color)[] rectangulos)
    {
        using var bitmap = new SKBitmap(ancho, alto, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var lienzo = new SKCanvas(bitmap))
        {
            lienzo.Clear(fondo);
            foreach (var r in rectangulos)
            {
                using var pintura = new SKPaint { Color = r.Color, IsAntialias = false };
                lienzo.DrawRect(r.X, r.Y, r.Ancho, r.Alto, pintura);
            }
        }

        bitmap.SetPixel(0, 0, SKColors.Black);
        return Guardar(bitmap, carpeta, nombre);
    }

    public static string CrearUniforme(string carpeta, string nombre, int ancho, int alto, SKColor color)
    {
        using var bitmap = new SKBitmap(ancho, alto, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(color);
        return Guardar(bitmap, carpeta, nombre);
    }

    private static string Guardar(SKBitmap bitmap, string carpeta, string nombre)
    {
        var ruta = Path.Combine(carpeta, nombre);
        using var imagen = SKImage.FromBitmap(bitmap);
        using var datos = imagen.Encode(SKEncodedImageFormat.Png, 100);
        using var archivo = File.Create(ruta);
        datos.SaveTo(archivo);
        return ruta;
    }
}
