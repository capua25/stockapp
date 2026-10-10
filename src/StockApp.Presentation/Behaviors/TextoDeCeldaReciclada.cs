using Avalonia;
using Avalonia.Controls;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Texto de una celda que la grilla recicla, alineado a la derecha (columnas numéricas): cada vez que cambia el Text
/// descarta el TextLayout que el TextBlock tenga armado, aunque el measure ya estuviera inválido.
/// <para>
/// Por qué hace falta: Avalonia (TextBlock) descarta su TextLayout solo cuando el measure pasa de válido a inválido.
/// En una búsqueda con la grilla ordenada la misma fila reciclada se re-bindea varias veces sin re-medirse; si entre
/// dos cambios se arma el TextLayout con el texto intermedio, los cambios siguientes no lo descartan y el measure (mismo
/// ancho disponible) lo reutiliza: el DesiredSize es el del número anterior. Un TextBlock alineado a la derecha toma
/// ese ancho y el número nuevo se corta con elipsis ("10,11" se vio "1…") aunque la columna sobre.
/// </para>
/// <para>
/// Cómo: se mide en el momento con ancho infinito, otro ancho disponible que el de la celda, así el TextBlock rearma
/// el layout con el texto actual y su DesiredSize pasa a ser el del número nuevo; cuando la celda lo mida con su
/// ancho real, el ancho distinto vuelve a rearmarlo. Se conserva la alineación del TextBlock (y no TextAlignment con
/// el TextBlock estirado) porque estirado el número cae en x fraccionaria y deja de coincidir con el mockup.
/// </para>
/// </summary>
public static class TextoDeCeldaReciclada
{
    public static readonly AttachedProperty<bool> ActivoProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, TextBlock, bool>("Activo");

    static TextoDeCeldaReciclada()
    {
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((tb, _) =>
        {
            if (GetActivo(tb))
                DescartarLayout(tb);
        });
    }

    public static bool GetActivo(TextBlock tb) => tb.GetValue(ActivoProperty);

    public static void SetActivo(TextBlock tb, bool valor) => tb.SetValue(ActivoProperty, valor);

    private static void DescartarLayout(TextBlock tb) => tb.Measure(Size.Infinity);
}
