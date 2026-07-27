using StockApp.Application.Backups;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Backups;

public class PoliticaRetencionTests
{
    private static readonly DateTime Ahora = new(2026, 7, 27, 15, 0, 0, DateTimeKind.Utc);

    private static CorridaBackup Corrida(DateTime finalizadaEn, string nombre) => new()
    {
        IniciadaEn = finalizadaEn.AddMinutes(-1),
        FinalizadaEn = finalizadaEn,
        Resultado = ResultadoBackup.Exitosa,
        NombreArchivo = nombre,
        TamanioBytes = 1024,
    };

    [Fact]
    public void DeterminarABorrar_MenosDeSeisCorridas_NoBorraNinguna()
    {
        var corridas = new List<CorridaBackup>
        {
            Corrida(Ahora.AddHours(-12), "c1"),
            Corrida(Ahora.AddHours(-24), "c2"),
            Corrida(Ahora.AddHours(-36), "c3"),
        };

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, Ahora);

        Assert.Empty(aBorrar);
    }

    [Fact]
    public void DeterminarABorrar_ExactamenteSeisCorridas_NoBorraNinguna()
    {
        var corridas = Enumerable.Range(0, 6)
            .Select(i => Corrida(Ahora.AddHours(-12 * i), $"c{i}"))
            .ToList();

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, Ahora);

        Assert.Empty(aBorrar);
    }

    [Fact]
    public void DeterminarABorrar_MuchasCorridasDistribuidasEnMeses_RetieneLaCombinacionDeLosTresConjuntosYBorraElResto()
    {
        // 90 corridas exitosas, una cada 12h, desde 45 días atrás hasta ahora — cubre de sobra
        // los 6 recientes + 7 días + 4 semanas, y dista mucho de esos rangos para el resto.
        var corridas = Enumerable.Range(0, 90)
            .Select(i => Corrida(Ahora.AddHours(-12 * i), $"c{i}"))
            .ToList();

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, Ahora);
        var nombresABorrar = aBorrar.Select(c => c.NombreArchivo).ToHashSet();

        // Las 6 más recientes (i=0..5) SIEMPRE se retienen.
        Assert.DoesNotContain("c0", nombresABorrar);
        Assert.DoesNotContain("c5", nombresABorrar);

        // Una corrida de hace 40 días no cae en ningún conjunto retenido (fuera de 6 recientes,
        // fuera de los últimos 7 días, fuera de las últimas 4 semanas = 28 días) -> se borra.
        var indiceViejo = corridas.FindIndex(c => (Ahora - c.FinalizadaEn).TotalDays >= 40);
        Assert.Contains($"c{indiceViejo}", nombresABorrar);

        // Se retuvo ALGO en el rango de la semana 3 (días 21-27 atrás) y ALGO en el rango de hace
        // 40 días no debería estar — confirma que hay borrado real, no "retener todo por las dudas".
        Assert.True(aBorrar.Count > 0);
        Assert.True(aBorrar.Count < corridas.Count);
    }

    [Fact]
    public void DeterminarABorrar_CruceDeMes_AgrupaPorBloqueRodanteSinImportarElMes()
    {
        // ahoraUtc = 2 de agosto. Corridas el 29, 30, 31 de julio y el 1, 2 de agosto: mismo
        // "día" cada una (offsetDias 0..4), deben quedar TODAS retenidas por la regla diaria
        // aunque el mes cambie a mitad del rango — sin caso especial en la implementación.
        var ahoraCruceDeMes = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var corridas = new List<CorridaBackup>
        {
            Corrida(new DateTime(2026, 7, 29, 3, 0, 0, DateTimeKind.Utc), "jul29"),
            Corrida(new DateTime(2026, 7, 30, 3, 0, 0, DateTimeKind.Utc), "jul30"),
            Corrida(new DateTime(2026, 7, 31, 3, 0, 0, DateTimeKind.Utc), "jul31"),
            Corrida(new DateTime(2026, 8, 1, 3, 0, 0, DateTimeKind.Utc), "ago1"),
            Corrida(new DateTime(2026, 8, 2, 3, 0, 0, DateTimeKind.Utc), "ago2"),
        };

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, ahoraCruceDeMes);

        Assert.Empty(aBorrar);
    }

    [Fact]
    public void DeterminarABorrar_SemanaActualParcial_RetieneLaUnicaCorridaDeEsaSemanaAunqueSeaUnaSola()
    {
        // Una sola corrida en toda la semana 0 (los últimos 7 días) — "semana parcial", debe
        // retenerse igual (no hace falta que la semana esté completa para que la regla aplique).
        var corridas = new List<CorridaBackup> { Corrida(Ahora.AddDays(-2), "unica") };

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, Ahora);

        Assert.Empty(aBorrar);
    }

    [Fact]
    public void DeterminarABorrar_ListaVacia_NoLanzaYDevuelveVacio()
    {
        var aBorrar = PoliticaRetencion.DeterminarABorrar(new List<CorridaBackup>(), Ahora);

        Assert.Empty(aBorrar);
    }
}
