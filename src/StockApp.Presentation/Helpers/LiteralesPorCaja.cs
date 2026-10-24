using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.Helpers;

/// <summary>Literales (fuentes de financiamiento) que se ofrecen para una caja: los combos de los formularios con caja
/// los filtran con esto, y el aviso de "caja sin literales" sale de acá.</summary>
public static class LiteralesPorCaja
{
    /// <summary>Deja en <paramref name="destino"/> las fuentes activas de la caja, más la <paramref name="historica"/>
    /// (la de un movimiento ya guardado, que puede estar dada de baja o ser de otra caja: la migración dejó pagos en caja
    /// Local con literales hoy POA) si no está. Quién la pasa decide cuándo corresponde: los formularios solo cuando la caja
    /// elegida es la caja original del movimiento.</summary>
    public static void Refrescar(ObservableCollection<FuenteFinanciamiento> destino, IEnumerable<FuenteFinanciamiento> activas,
        Caja caja, FuenteFinanciamiento? historica = null)
    {
        destino.Clear();
        foreach (var fuente in activas.Where(f => f.Caja == caja))
            destino.Add(fuente);
        if (historica is not null && destino.All(f => f.Id != historica.Id))
            destino.Add(historica);
    }

    /// <summary>Aviso de la caja sin literales asignados; null si hay al menos uno.</summary>
    public static string? Aviso(Caja caja, int cantidad) => cantidad > 0
        ? null
        : $"No hay literales asignados a la caja {caja.Nombre()}; configuralos en Fuentes de financiamiento";
}
