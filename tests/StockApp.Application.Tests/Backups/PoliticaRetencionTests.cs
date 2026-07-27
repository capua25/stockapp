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
    public void DeterminarABorrar_SemanaActualParcialConMasDeSeisCorridas_RetieneUnaPorSemanaYBorraElResto()
    {
        // Las 6 más recientes ("6 recientes") caen TODAS dentro de la semana actual (offsets
        // 1..6 días atrás) y encima falta la de hoy (offset 0) -> semana actual "parcial", no
        // completa. Con exactamente 6 corridas ahí, ocupan los 6 cupos de "recientes" por
        // completo y a propósito: así cualquier corrida de semanas anteriores (offset >= 7)
        // queda SIEMPRE fuera de "recientes" sin importar cuántas más se agreguen, y lo único
        // que puede salvarla es el bucket semanal — que es lo que este test aísla y verifica.
        var actual1 = Corrida(Ahora.AddDays(-1), "actual1");
        var actual2 = Corrida(Ahora.AddDays(-2), "actual2");
        var actual3 = Corrida(Ahora.AddDays(-3), "actual3");
        var actual4 = Corrida(Ahora.AddDays(-4), "actual4");
        var actual5 = Corrida(Ahora.AddDays(-5), "actual5");
        var actual6 = Corrida(Ahora.AddDays(-6), "actual6");

        // Semana 1 (7-13 días atrás): dos corridas -> el bucket debe retener SOLO la más
        // reciente de la semana (week1Keeper) y descartar la otra (week1Extra).
        var week1Keeper = Corrida(Ahora.AddDays(-8), "week1Keeper");
        var week1Extra = Corrida(Ahora.AddDays(-12), "week1Extra");

        // Semana 2 (14-20 días atrás): mismo patrón.
        var week2Keeper = Corrida(Ahora.AddDays(-15), "week2Keeper");
        var week2Extra = Corrida(Ahora.AddDays(-19), "week2Extra");

        // Semana 3 (21-27 días atrás): una sola corrida, debe retenerse igual.
        var week3Keeper = Corrida(Ahora.AddDays(-22), "week3Keeper");

        // Fuera de las 4 semanas de retención (> 27 días) y fuera de las 6 recientes -> se borra.
        var fueraDeTodo = Corrida(Ahora.AddDays(-40), "fueraDeTodo");

        var corridas = new List<CorridaBackup>
        {
            actual1, actual2, actual3, actual4, actual5, actual6,
            week1Keeper, week1Extra, week2Keeper, week2Extra, week3Keeper, fueraDeTodo,
        };

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, Ahora);
        var nombresABorrar = aBorrar.Select(c => c.NombreArchivo).ToHashSet();

        Assert.Equal(3, nombresABorrar.Count);
        Assert.Contains("week1Extra", nombresABorrar);
        Assert.Contains("week2Extra", nombresABorrar);
        Assert.Contains("fueraDeTodo", nombresABorrar);
        Assert.DoesNotContain("actual1", nombresABorrar);
        Assert.DoesNotContain("actual2", nombresABorrar);
        Assert.DoesNotContain("actual3", nombresABorrar);
        Assert.DoesNotContain("actual4", nombresABorrar);
        Assert.DoesNotContain("actual5", nombresABorrar);
        Assert.DoesNotContain("actual6", nombresABorrar);
        Assert.DoesNotContain("week1Keeper", nombresABorrar);
        Assert.DoesNotContain("week2Keeper", nombresABorrar);
        Assert.DoesNotContain("week3Keeper", nombresABorrar);
    }

    [Fact]
    public void DeterminarABorrar_DiasSinCorridaEnLaVentanaDiaria_SaltaElHuecoSinRomperLaSeleccion()
    {
        // PoliticaRetencion recibe SOLO corridas Exitosas (el filtrado por Resultado es
        // responsabilidad de quien la llama, no de esta función pura — ver doc del método).
        // Por eso el caso borde real que le corresponde a esta función no es "hay corridas
        // Fallidas mezcladas", sino "hay días calendario sin NINGUNA corrida" dentro de la
        // ventana diaria de 7 días: eso es lo que ejercita la rama `delDia is not null` cuando
        // NO hay candidato, sin romper el resto de la selección.
        var d0 = Corrida(Ahora, "d0"); // offset 0
        var d2 = Corrida(Ahora.AddDays(-2), "d2"); // offset 2
        var d4 = Corrida(Ahora.AddDays(-4), "d4"); // offset 4
        var d6 = Corrida(Ahora.AddDays(-6), "d6"); // offset 6
        // offsets 1, 3 y 5 quedan sin ninguna corrida -> huecos reales en la ventana diaria.
        var w1 = Corrida(Ahora.AddDays(-10), "w1"); // semana 1
        var w2 = Corrida(Ahora.AddDays(-17), "w2"); // semana 2
        var w3 = Corrida(Ahora.AddDays(-25), "w3"); // semana 3 — fuera de las 6 más recientes, sólo la retiene el bucket semanal
        var muyVieja = Corrida(Ahora.AddDays(-87), "muyVieja"); // fuera de recientes, diaria y semanal -> se borra

        var corridas = new List<CorridaBackup> { d0, d2, d4, d6, w1, w2, w3, muyVieja };

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, Ahora);
        var nombresABorrar = aBorrar.Select(c => c.NombreArchivo).ToHashSet();

        Assert.Single(nombresABorrar);
        Assert.Contains("muyVieja", nombresABorrar);
        Assert.DoesNotContain("d0", nombresABorrar);
        Assert.DoesNotContain("d2", nombresABorrar);
        Assert.DoesNotContain("d4", nombresABorrar);
        Assert.DoesNotContain("d6", nombresABorrar);
        Assert.DoesNotContain("w1", nombresABorrar);
        Assert.DoesNotContain("w2", nombresABorrar);
        Assert.DoesNotContain("w3", nombresABorrar);
    }

    [Fact]
    public void DeterminarABorrar_ListaVacia_NoLanzaYDevuelveVacio()
    {
        var aBorrar = PoliticaRetencion.DeterminarABorrar(new List<CorridaBackup>(), Ahora);

        Assert.Empty(aBorrar);
    }
}
