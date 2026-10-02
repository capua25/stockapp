using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Acceso;

/// <summary>
/// Marca de agua del logo de Carmelo en el Login (P5, aprobada y mergeada a main el 2026-07-10).
/// La comparación de píxeles la oculta (AccesoComparacionTests); este es el único guardián de que
/// sigue estando, con su opacidad, su escala y su lugar.
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class LoginMarcaAguaTests
{
    private const double OpacidadAprobada = 0.28;
    private const double EscalaAprobada = 0.85;

    private static string Png(string nombre) => Path.Combine(ComparacionVisual.CarpetaSalida, "acceso-login-marca-agua", nombre);

    private static Image Marca(LoginView vista) =>
        vista.FindControl<Image>("MarcaAgua") ?? throw new InvalidOperationException("LoginView perdió Image#MarcaAgua.");

    [AvaloniaFact]
    public void Marca_ConservaFuenteOpacidadEscalaYLugar_YSoloDifiereDentroDeSuRegion()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Png("x"))!);
        Size fuente = default;

        // Con la marca (la ventana abierta: los Style de la vista solo aplican montada).
        RenderizadorVista.RenderizarAPng(new LoginView(), DatosAcceso.Login(), Png("con.png"), v =>
        {
            var marca = Marca((LoginView)v);
            Assert.NotNull(marca.Source);
            fuente = marca.Source!.Size;
            Assert.Equal(new Size(2804, 1448), fuente);
            Assert.Equal(OpacidadAprobada, marca.Opacity, 3);
            var transformacion = Assert.IsAssignableFrom<ITransform>(marca.RenderTransform).Value;
            Assert.Equal(EscalaAprobada, transformacion.M11, 3);
            Assert.Equal(EscalaAprobada, transformacion.M22, 3);
            Assert.Equal(Stretch.Uniform, marca.Stretch);
            Assert.Equal(HorizontalAlignment.Center, marca.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, marca.VerticalAlignment);
            // Detrás de la card: el ScrollViewer con la card viene DESPUÉS en el Grid.
            var panel = Assert.IsAssignableFrom<Panel>(marca.Parent);
            Assert.True(panel.Children.IndexOf(marca) < panel.Children.IndexOf(panel.Children.OfType<ScrollViewer>().Single()));
        });

        // Sin la marca: la diferencia entre las dos capturas ES la marca.
        var vistaSin = new LoginView();
        Marca(vistaSin).IsVisible = false;
        RenderizadorVista.RenderizarAPng(vistaSin, DatosAcceso.Login(), Png("sin.png"));

        var (cantidad, deltaMaximo, caja) = Diferencias(Png("con.png"), Png("sin.png"));

        // Región esperada: Uniform en 1280x760, por la escala aprobada, centrada (+-2 px).
        var uniforme = Math.Min(Lienzo.Ancho / fuente.Width, Lienzo.Alto / fuente.Height);
        var ancho = fuente.Width * uniforme * EscalaAprobada;
        var alto = fuente.Height * uniforme * EscalaAprobada;
        var esperada = new Rect((Lienzo.Ancho - ancho) / 2, (Lienzo.Alto - alto) / 2, ancho, alto).Inflate(2);
        Assert.True(cantidad > 1000, $"La marca casi no se ve: {cantidad} píxeles difieren.");
        Assert.True(esperada.Contains(caja), $"La marca se dibuja fuera de su región: {caja} no está en {esperada}.");
        // Con opacidad 0.28 ningún canal puede moverse más de 0.28 * 255 = 71.4 respecto del fondo.
        Assert.InRange(deltaMaximo, 8, 72);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("login-error")]
    [InlineData("login-acceso-limitado")]
    public void Mockup_DeclaraLaMismaMarcaDeAguaQueLaApp(string mockup)
    {
        var html = File.ReadAllText(Path.Combine(RutasRepo.CarpetaMockups, "acceso", mockup + ".html"));

        Assert.Contains("carmelo-original.png", html);
        // Dentro de la regla, no en cualquier parte del archivo: el comentario del mockup también menciona
        // 0.28 y scale(0.85) y dejaba pasar el test aunque se quitaran de la regla (verificado por mutación).
        var regla = Regex.Match(html, @"\.marca-agua\s*\{(?<cuerpo>[^}]*)\}");
        Assert.True(regla.Success, "El mockup perdió la regla .marca-agua.");
        Assert.Contains("opacity: " + OpacidadAprobada.ToString(CultureInfo.InvariantCulture), regla.Groups["cuerpo"].Value);
        Assert.Contains("scale(" + EscalaAprobada.ToString(CultureInfo.InvariantCulture) + ")", regla.Groups["cuerpo"].Value);
    }

    private static (int Cantidad, int DeltaMaximo, Rect Caja) Diferencias(string rutaA, string rutaB)
    {
        using var a = SKBitmap.Decode(rutaA);
        using var b = SKBitmap.Decode(rutaB);
        int cantidad = 0, delta = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                var d = Math.Max(Math.Abs(pa.Red - pb.Red), Math.Max(Math.Abs(pa.Green - pb.Green), Math.Abs(pa.Blue - pb.Blue)));
                if (d == 0)
                {
                    continue;
                }

                cantidad++;
                delta = Math.Max(delta, d);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return (cantidad, delta, cantidad == 0 ? default : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
    }
}
