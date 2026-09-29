using Avalonia.Controls;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Card blanca con borde, radio 10 y la única sombra del sistema (spec §3.3). ContentControl y
/// no UserControl: el contenido queda en el NameScope de la vista que lo usa.
/// </summary>
public class TarjetaContenido : ContentControl
{
}
