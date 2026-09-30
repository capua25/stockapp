using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Renderiza una vista real a PNG de 1280x760 con Skia. Requiere correr dentro de un
/// [AvaloniaFact] de este proyecto (VisualTestApp: UseSkia + UseHeadlessDrawing=false).
/// </summary>
public static class RenderizadorVista
{
    /// <param name="inspeccionar">Corre con la ventana todavía abierta, después de guardar el PNG:
    /// al cerrarla la vista se desengancha de los estilos de la ventana y medirla ya no da lo mismo.</param>
    public static void RenderizarAPng(Control vista, object? viewModel, string rutaPng, Action<Control>? inspeccionar = null)
    {
        if (!Avalonia.Application.Current!.TryGetResource("V2FondoBrush", ThemeVariant.Light, out var fondo)
            || fondo is not IBrush brushFondo)
        {
            throw new InvalidOperationException("V2FondoBrush no se resuelve: Themes/V2/Tokens.axaml no está cargado.");
        }

        if (viewModel is not null)
        {
            vista.DataContext = viewModel;
        }

        var ventana = new Window
        {
            Width = Lienzo.Ancho,
            Height = Lienzo.Alto,
            SizeToContent = SizeToContent.Manual,
            Background = brushFondo,
            Content = vista,
        };

        ventana.Show();
        // Dos vueltas: la primera procesa Loaded (donde algunas vistas cargan o setean estado) y
        // la segunda el layout que eso invalida. Sin la segunda, el frame puede salir sin ese cambio.
        Dispatcher.UIThread.RunJobs();
        ventana.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var frame = ventana.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("El render headless no produjo frame: revisar UseSkia y UseHeadlessDrawing=false.");
        if (frame.PixelSize.Width != Lienzo.Ancho || frame.PixelSize.Height != Lienzo.Alto)
        {
            throw new InvalidOperationException(
                $"Frame de {frame.PixelSize.Width}x{frame.PixelSize.Height}, se esperaba {Lienzo.Ancho}x{Lienzo.Alto}.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(rutaPng))!);
        frame.Save(rutaPng);
        inspeccionar?.Invoke(vista);
        ventana.Close();
    }
}
