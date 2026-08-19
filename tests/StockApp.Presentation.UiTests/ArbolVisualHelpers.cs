using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Helpers de árbol visual compartidos por todo el banco de pruebas de UI.
/// </summary>
public static class ArbolVisual
{
    /// <summary>
    /// IsVisible propio de un control NO cae en cascada al valor del ancestro en Avalonia: un
    /// TextBox dentro de un StackPanel con IsVisible=False sigue reportando su propio
    /// IsVisible=True, y GetVisualDescendants lo encuentra igual. Camina la cadena de ancestros
    /// para saber si de verdad está en pantalla. Imprescindible para tests de gates de permisos:
    /// sin esto, un botón gateado dentro de un contenedor oculto da falso verde.
    /// </summary>
    public static bool EsVisibleEnArbol(Visual visual)
    {
        for (Visual? actual = visual; actual is not null; actual = actual.GetVisualParent())
        {
            if (actual is Control c && !c.IsVisible) return false;
        }
        return true;
    }
}
