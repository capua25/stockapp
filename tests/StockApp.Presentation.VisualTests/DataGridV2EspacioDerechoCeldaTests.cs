using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Guardián del "espacio horrible" a la derecha del texto de las celdas del tema v2: el texto
/// que no entra en una columna angosta quedaba cortado ~12 px ANTES del borde derecho de la celda
/// (el padding derecho simétrico de 12 se descontaba del lugar del texto). Se mide con render Skia
/// real: distancia en px entre el último píxel de tinta y el borde derecho de la celda. Tolerancia
/// de 6 px = el padding derecho de 4 + hasta 2 px de espacio propio del glifo (side bearing y
/// antialiasing: medido 5 px con una cifra alineada a la derecha y padding 4). Con el padding
/// simétrico anterior de 12 la medición daba 13 px, así que 6 discrimina sin ser frágil.
/// </summary>
public class DataGridV2EspacioDerechoCeldaTests
{
    private const int EspacioMaximoPx = 6;

    private sealed class Fila(string texto)
    {
        public string Texto { get; } = texto;
    }

    private static (Window Ventana, DataGrid Grilla) Montar()
    {
        var grilla = new DataGrid
        {
            AutoGenerateColumns = false,
            Height = 200,
            Columns =
            {
                // Angosta: "21:57:33" (~50 px) no entra en los 60 menos el padding.
                new DataGridTextColumn { Header = "HORA", Binding = new Binding(nameof(Fila.Texto)), Width = new DataGridLength(60) },
                // Numérica: el texto entra de sobra, alineado a la derecha.
                new DataGridTextColumn { Header = "VALOR", Binding = new Binding(nameof(Fila.Texto)), Width = new DataGridLength(150), CellStyleClasses = { "num" } },
            },
        };
        var raiz = new UserControl { Classes = { "ui2" }, Content = grilla };
        var ventana = new Window { Width = 400, Height = 300, Content = raiz };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        grilla.ItemsSource = new[] { new Fila("21:57:33") };
        Dispatcher.UIThread.RunJobs();
        ventana.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (ventana, grilla);
    }

    private static DataGridCell CeldaDeDatos(DataGrid grilla, bool num) =>
        grilla.GetVisualDescendants().OfType<DataGridCell>()
            .Where(c => c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "CellTextBlock"))
            .Single(c => c.Classes.Contains("num") == num);

    /// <summary>Px entre la última columna con tinta dentro de la celda y el borde derecho de la celda.</summary>
    private static (int Espacio, string Desglose) MedirEspacioDerecho(Window ventana, DataGridCell celda)
    {
        var frame = ventana.CaptureRenderedFrame() ?? throw new InvalidOperationException("Sin frame.");
        var origen = celda.TranslatePoint(new Point(0, 0), ventana) ?? throw new InvalidOperationException("Celda fuera de la ventana.");
        var izq = (int)Math.Round(origen.X);
        var arriba = (int)Math.Round(origen.Y);
        var ancho = (int)Math.Round(celda.Bounds.Width);
        var alto = (int)Math.Round(celda.Bounds.Height);

        using var fb = frame.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        int Pix(int x, int y, int canal) => bytes[y * fb.RowBytes + x * 4 + canal];

        // Fondo de referencia: esquina superior izquierda de la celda, adentro del separador.
        var refX = izq + 1;
        var refY = arriba + 2;
        var ultimaTinta = -1;
        // Se recorre desde el borde derecho hacia adentro; se ignora el último px (línea de columna).
        for (var x = izq + ancho - 2; x >= izq && ultimaTinta < 0; x--)
        {
            for (var y = arriba + 4; y < arriba + alto - 4; y++)
            {
                var difiere = Enumerable.Range(0, 3).Any(c => Math.Abs(Pix(x, y, c) - Pix(refX, refY, c)) > 40);
                if (difiere)
                {
                    ultimaTinta = x;
                    break;
                }
            }
        }

        var texto = celda.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "CellTextBlock");
        var textoDer = texto.TranslatePoint(new Point(0, 0), celda)!.Value.X;
        var espacio = izq + ancho - 1 - ultimaTinta;
        var desglose =
            $"celda {ancho}px, Padding={celda.Padding}, TextBlock x={textoDer:0.#} w={texto.Bounds.Width:0.#} " +
            $"desired={texto.DesiredSize.Width:0.#} trimming={texto.TextTrimming}, " +
            $"tinta hasta x={ultimaTinta - izq} (de {ancho}), espacio={espacio}px";
        return (espacio, desglose);
    }

    [AvaloniaFact]
    public void ColumnaAngostaConTextoQueNoEntra_DejaNoMasDe6PxALaDerechaDelUltimoPixelDeTinta()
    {
        var (ventana, grilla) = Montar();
        var (espacio, desglose) = MedirEspacioDerecho(ventana, CeldaDeDatos(grilla, num: false));
        Assert.True(espacio <= EspacioMaximoPx, $"Espacio a la derecha > {EspacioMaximoPx}px: {desglose}");
    }

    [AvaloniaFact]
    public void ColumnaNumericaAlineadaALaDerecha_DejaNoMasDe6PxEntreElTextoYElBordeDeLaCelda()
    {
        var (ventana, grilla) = Montar();
        var (espacio, desglose) = MedirEspacioDerecho(ventana, CeldaDeDatos(grilla, num: true));
        Assert.True(espacio <= EspacioMaximoPx, $"Espacio a la derecha > {EspacioMaximoPx}px: {desglose}");
    }
}
