using System;
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
    ///
    /// Contrato: un control null NO es visible ni oculto, es "no se encontró" (x:Name faltante o
    /// mal escrito en el test). Tira ArgumentNullException en vez de devolver un bool, así un
    /// FindControl fallido no se confunde con "existe pero está oculto" ni da un falso verde.
    /// </summary>
    public static bool EsVisibleEnArbol(Visual visual)
    {
        if (visual is null)
            throw new ArgumentNullException(nameof(visual),
                "El control no se encontró en el árbol (¿x:Name faltante en la View o nombre mal escrito en el test?); no se puede decidir si es visible.");

        for (Visual? actual = visual; actual is not null; actual = actual.GetVisualParent())
        {
            if (actual is Control c && !c.IsVisible) return false;
        }
        return true;
    }
}
