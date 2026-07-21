using StockApp.Infrastructure.Finanzas;
using Xunit;

namespace StockApp.Infrastructure.Tests.Finanzas;

/// <summary>
/// Red de seguridad de integración: parsea la planilla POA REAL del municipio
/// (PlanillaPoa2026.ods, copiada al output vía CopyToOutputDirectory) para blindar el corte de
/// lectura de cada hoja de línea. Este test es el que FALTÓ y dejó pasar dos veces el bug de la
/// fila de TOTAL fantasma: los fixtures sintéticos son fáciles de "acomodar" para dar verde, la
/// planilla real no. Si este test se pone rojo, la lógica de corte volvió a romperse.
/// </summary>
public class PlanillaOdsParserPoaRealFixtureTests
{
    private static PlanillaOdsParser Parser => new();

    private static Stream AbrirPlanillaReal()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Finanzas", "PlanillaPoa2026.ods");
        Assert.True(File.Exists(ruta), $"Falta el fixture real 'PlanillaPoa2026.ods' en {ruta}.");
        return File.OpenRead(ruta);
    }

    // Conteo de movimientos esperado por hoja, verificado a mano contra las 15 hojas de línea de
    // PlanillaPoa2026.ods. Es EL JUEZ del corte de lectura: la fila de TOTAL (última fila con
    // contenido de cada hoja) NUNCA debe contar como movimiento, y las filas vacías o con texto
    // en columnas ajenas (COMPOSTERAS) tampoco. La muestra parcial dejó pasar el bug de PRENSA
    // dos veces, así que acá se assertean LAS 15, no una selección.
    private static readonly (string Hoja, int Movimientos)[] ConteoEsperadoPorHoja =
    {
        ("RAMBLA", 0),
        ("ARQ. Y,O ASESORAMIENTO TECNICO", 0),
        ("MEJORAS EN BARRACONES", 7),
        ("MEJORAS EN MARCELO BIANCHI", 0),
        ("MEJORAS TEATRO DE VERANO", 3),
        ("HERRAMIENTAS", 1),
        ("LUCES DE NAVIDAD", 0),
        ("CARPETA ASFÁLTICA", 1),
        ("ROPA PARA FUNCIONARIOS", 0),
        ("COMPRA VEHÍCULO", 0),
        ("MEJORAS EN PLUVIALES", 1),
        ("EVENTOS", 1),
        ("PRENSA", 1),
        ("COMPRA CONTENEDORES", 1),
        ("COMPOSTERAS Y COMPACTADORAS", 0),
    };

    [Fact]
    public void ParsearPoa_PlanillaReal_ConteoDeMovimientosDeLasQuinceHojas_CoincideConLaGeometriaReal()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        // Se agrupa el assert por nombre de hoja para que un fallo diga EXACTAMENTE qué hoja
        // (y con qué conteo) se desvió, en vez de un "esperaba 16, obtuve 17" mudo.
        var esperado = ConteoEsperadoPorHoja
            .Select(e => $"{e.Hoja}={e.Movimientos}")
            .ToArray();
        var real = ConteoEsperadoPorHoja
            .Select(e =>
            {
                var linea = resultado.Lineas.Single(l => l.Hoja == e.Hoja);
                return $"{e.Hoja}={linea.Movimientos.Count}";
            })
            .ToArray();

        Assert.Equal(esperado, real);

        // Total consolidado: 16 movimientos repartidos en 8 hojas.
        Assert.Equal(16, resultado.Lineas.Sum(l => l.Movimientos.Count));
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_LeeLosSaldosConsolidados()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        Assert.Equal(6643349m, resultado.SaldosTotales.SaldoLiteralB);
        Assert.Equal(4654206m, resultado.SaldosTotales.SaldoLiteralC);
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_HerramientasTieneUnSoloMovimientoSinContarElTotal()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        var herramientas = resultado.Lineas.Single(l => l.Hoja == "HERRAMIENTAS");
        var movimiento = Assert.Single(herramientas.Movimientos);
        Assert.Equal(140000m, movimiento.Importe);
        // El importe 140000 es a la vez el del ÚNICO movimiento y el de la fila de TOTAL del
        // fondo. Si el total colara como movimiento fantasma, habría 2 filas con 140000.
        Assert.Equal(1, herramientas.Movimientos.Count(m => m.Importe == 140000m));
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_CarpetaAsfalticaTieneUnMovimientoSinFacturaYSinTotalDuplicado()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        var carpeta = resultado.Lineas.Single(l => l.Hoja == "CARPETA ASFÁLTICA");
        var movimiento = Assert.Single(carpeta.Movimientos);
        Assert.Null(movimiento.Factura);
        Assert.Equal(5500000m, movimiento.Importe);
        // 5500000 aparece como movimiento (sin factura, arriba del hueco) Y como fila de TOTAL.
        // Debe contarse UNA sola vez.
        Assert.Equal(1, carpeta.Movimientos.Count(m => m.Importe == 5500000m));
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_MejorasEnBarraconesTieneSieteMovimientosSinContarElTotal()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        var barracones = resultado.Lineas.Single(l => l.Hoja == "MEJORAS EN BARRACONES");
        Assert.Equal(7, barracones.Movimientos.Count);
        // La suma de los 7 movimientos reales coincide con la fila de TOTAL (62329); esa fila NO
        // debe aparecer como un octavo movimiento con importe 62329.
        Assert.Equal(62329m, barracones.Movimientos.Sum(m => m.Importe ?? 0m));
        Assert.DoesNotContain(barracones.Movimientos, m => m.Importe == 62329m);
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_MejorasTeatroDeVeranoTieneTresMovimientos()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        var teatro = resultado.Lineas.Single(l => l.Hoja == "MEJORAS TEATRO DE VERANO");
        Assert.Equal(3, teatro.Movimientos.Count);
        Assert.Equal(47070m, teatro.Movimientos.Sum(m => m.Importe ?? 0m));
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_PrensaTieneUnMovimientoPeseALasDosFilasVaciasPrevias()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        // PRENSA tiene 2 filas vacías entre el header y su único movimiento. Esas vacías NO
        // deben cortar la lectura (el bug que colaba dos veces): el movimiento (360000) queda
        // más abajo y debe contarse.
        var prensa = resultado.Lineas.Single(l => l.Hoja == "PRENSA");
        var movimiento = Assert.Single(prensa.Movimientos);
        Assert.Equal(360000m, movimiento.Importe);
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_ComposterasNoCuentaLaFilaDeTextoNoTipado()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        // COMPOSTERAS trae una fila con texto ("literal C"/"literal B") en columnas que NO son
        // ninguno de los 5 campos tipados (FACTURA/ORDEN/PROVEEDOR/GASTO/IMPORTE): se salta, no
        // es un movimiento.
        var composteras = resultado.Lineas.Single(l => l.Hoja == "COMPOSTERAS Y COMPACTADORAS");
        Assert.Empty(composteras.Movimientos);
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_ComposterasTieneLasDosAsignacionesDeFinanciamientoMixto()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        // F5b: COMPOSTERAS Y COMPACTADORAS es la ÚNICA hoja de financiamiento mixto de la
        // planilla real — se reparte en DOS asignaciones (LITERAL C y LITERAL B) en vez de la
        // fila agregada (1.500.000 = 1.407.252 + 92.748) que el parser F5a leía por error.
        var composteras = resultado.Lineas.Single(l => l.Hoja == "COMPOSTERAS Y COMPACTADORAS");
        Assert.Equal(2, composteras.Asignaciones.Count);

        var asignacionC = composteras.Asignaciones.Single(a => a.Literal == "C");
        Assert.Equal(1407252m, asignacionC.Presupuesto);
        Assert.Equal(1407252m, asignacionC.Saldo);

        var asignacionB = composteras.Asignaciones.Single(a => a.Literal == "B");
        Assert.Equal(92748m, asignacionB.Presupuesto);
        Assert.Equal(92748m, asignacionB.Saldo);
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_CadaHojaSimpleTieneExactamenteUnaAsignacion_SalvoComposterasQueTieneDos()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        // Guard anti-regresión: hoy solo COMPOSTERAS Y COMPACTADORAS es de financiamiento mixto
        // (2 asignaciones). Todas las demás hojas de línea son simples y deben quedar en EXACTAMENTE
        // 1 asignación. Sin este test, un bug futuro que hiciera emitir 2 asignaciones en una hoja
        // simple no se detectaría (los tests existentes solo cubren ≥1 / conteo de movimientos).
        const string hojaDeFinanciamientoMixto = "COMPOSTERAS Y COMPACTADORAS";

        foreach (var linea in resultado.Lineas)
        {
            var esperado = linea.Hoja == hojaDeFinanciamientoMixto ? 2 : 1;
            Assert.True(
                linea.Asignaciones.Count == esperado,
                $"La hoja '{linea.Hoja}' tiene {linea.Asignaciones.Count} asignación(es), se esperaba {esperado}.");
        }
    }
}
