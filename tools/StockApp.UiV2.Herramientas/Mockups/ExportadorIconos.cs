using Optris.Icons.Avalonia.MaterialDesign;

namespace StockApp.UiV2.Herramientas.Mockups;

/// <summary>
/// SVG inline de un ícono MDI a partir del MISMO proveedor que usa la app
/// (MaterialDesignIconProvider de Optris.Icons.Avalonia): mismo path, mismo viewBox.
/// </summary>
public static class ExportadorIconos
{
    public static string Svg(string clave, int tamano = 16)
    {
        Optris.Icons.Avalonia.Models.IconModel icono;
        try
        {
            icono = new MaterialDesignIconProvider().GetIcon(clave);
        }
        catch (KeyNotFoundException ex)
        {
            // El proveedor lanza KeyNotFoundException, que Cli.Ejecutar NO captura: se convierte
            // a ArgumentException (sí capturada) para que "icono-svg <clave-mala>" salga con
            // exit 1 y el mensaje en stderr, no con una excepción sin manejar.
            throw new ArgumentException(ex.Message, nameof(clave), ex);
        }

        var caja = icono.ViewBox;
        return $"<svg class=\"v2-icono\" width=\"{tamano}\" height=\"{tamano}\" "
             + $"viewBox=\"{caja.X} {caja.Y} {caja.Width} {caja.Height}\" aria-hidden=\"true\">"
             + $"<path fill=\"currentColor\" d=\"{(string)icono.Path}\"/></svg>";
    }
}
