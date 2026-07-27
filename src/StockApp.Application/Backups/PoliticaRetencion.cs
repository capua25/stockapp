using StockApp.Domain.Entities;

namespace StockApp.Application.Backups;

/// <summary>
/// Retención grandfather-father-son de backups exitosos (spec Backups §3 decisión 5): los 6
/// más recientes + el último de cada uno de los últimos 7 días + el último de cada una de las
/// últimas 4 semanas (bloques rodantes de 7 días desde ahoraUtc, no calendario). Función pura:
/// sin DB, sin filesystem, sin reloj real — ahoraUtc entra como parámetro para que los tests
/// sean 100% determinísticos.
/// </summary>
public static class PoliticaRetencion
{
    private const int CantidadRecientes = 6;
    private const int DiasRetencionDiaria = 7;
    private const int SemanasRetencionSemanal = 4;

    /// <summary>Recibe SOLO corridas Exitosas (las Fallidas no participan de la retención —
    /// nunca tuvieron archivo que conservar). Devuelve las que hay que borrar de disco + DB.</summary>
    public static IReadOnlyList<CorridaBackup> DeterminarABorrar(
        IReadOnlyList<CorridaBackup> corridasExitosas, DateTime ahoraUtc)
    {
        var ordenadas = corridasExitosas.OrderByDescending(c => c.FinalizadaEn).ToList();
        var retener = new HashSet<CorridaBackup>();

        foreach (var c in ordenadas.Take(CantidadRecientes))
            retener.Add(c);

        for (var offsetDias = 0; offsetDias < DiasRetencionDiaria; offsetDias++)
        {
            var dia = ahoraUtc.Date.AddDays(-offsetDias);
            var delDia = ordenadas.FirstOrDefault(c => c.FinalizadaEn.Date == dia);
            if (delDia is not null)
                retener.Add(delDia);
        }

        for (var semana = 0; semana < SemanasRetencionSemanal; semana++)
        {
            var hasta = ahoraUtc.Date.AddDays(-7 * semana);
            var desde = hasta.AddDays(-6);
            var deLaSemana = ordenadas.FirstOrDefault(c => c.FinalizadaEn.Date >= desde && c.FinalizadaEn.Date <= hasta);
            if (deLaSemana is not null)
                retener.Add(deLaSemana);
        }

        return ordenadas.Where(c => !retener.Contains(c)).ToList();
    }
}
