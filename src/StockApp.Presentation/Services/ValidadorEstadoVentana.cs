using System.Collections.Generic;
using Avalonia;

namespace StockApp.Presentation.Services;

/// <summary>
/// Lógica pura (sin dependencias de UI real) para decidir si un <see cref="EstadoVentana"/>
/// guardado sigue siendo válido para la configuración de pantallas actual. Extraída del
/// wiring de la ventana para poder testearla sin levantar una ventana real (ej. caso del
/// monitor desenchufado desde la última vez que se guardó el estado).
/// </summary>
public static class ValidadorEstadoVentana
{
    /// <summary>
    /// Indica si el rectángulo del <paramref name="estado"/> intersecta al menos una de las
    /// <paramref name="pantallas"/> dadas. Si no intersecta ninguna, la posición/tamaño
    /// guardados quedaron fuera del área visible (ej. se desconectó un monitor) y no deben
    /// aplicarse.
    /// </summary>
    public static bool EsVisibleEn(EstadoVentana estado, IEnumerable<PixelRect> pantallas)
    {
        var rectanguloVentana = new PixelRect(estado.X, estado.Y, (int)estado.Ancho, (int)estado.Alto);

        foreach (var pantalla in pantallas)
        {
            if (rectanguloVentana.Intersects(pantalla))
                return true;
        }

        return false;
    }
}
