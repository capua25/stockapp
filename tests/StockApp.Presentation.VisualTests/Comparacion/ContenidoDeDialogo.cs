using Avalonia.Controls;
using Avalonia.Layout;

namespace StockApp.Presentation.VisualTests.Comparacion;

/// <summary>
/// Los diálogos son Window y RenderizadorVista no puede meter un Window dentro de su ventana
/// ("already has a visual parent"). Se compara el ÁREA CLIENTE: el Border raíz del diálogo (que
/// lleva ui2 y su fondo) desenganchado del Window, centrado y con el ancho del diálogo. La barra
/// de título es del sistema operativo y no se compara.
/// </summary>
public static class ContenidoDeDialogo
{
    public static Control Extraer(Window dialogo)
    {
        var contenido = dialogo.Content as Control
            ?? throw new InvalidOperationException($"{dialogo.GetType().Name} no tiene un Control como contenido.");
        dialogo.Content = null;
        return new Border
        {
            Width = dialogo.Width,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = contenido,
        };
    }
}
