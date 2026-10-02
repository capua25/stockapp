using StockApp.UiV2.Herramientas;
using StockApp.UiV2.Herramientas.Mockups;

namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// Convención de herramientas.md: el valor de una celda numérica va en un único span, que hace de
/// TextBlock (ceil del ancho como Avalonia). Sin el span la cifra queda hasta 1px corrida y la
/// comparación lo absorbe como ruido: nadie lo ve (ledger Fase 0, Task 17).
/// </summary>
public class VerificadorConvencionesTests
{
    [Fact]
    public void CeldaConUnSpan_Cumple() =>
        Assert.Empty(VerificadorConvenciones.CeldasNumericasSinSpan(
            """<div class="v2-grilla-celda v2-num"><span>12.50</span></div>"""));

    [Fact]
    public void CeldaConTextoSuelto_NoCumple() =>
        Assert.Single(VerificadorConvenciones.CeldasNumericasSinSpan(
            """<div class="v2-grilla-celda v2-num">12.50</div>"""));

    [Fact]
    public void CeldaConDosSpans_NoCumple() =>
        Assert.Single(VerificadorConvenciones.CeldasNumericasSinSpan(
            """<div class="v2-num"><span>12</span><span>.50</span></div>"""));

    [Fact]
    public void ClaseParecida_NoCuenta() =>
        Assert.Empty(VerificadorConvenciones.CeldasNumericasSinSpan(
            """<div class="v2-numero">12.50</div>"""));

    [Fact]
    public void ContenedorSinV2Num_NoSeMira() =>
        Assert.Empty(VerificadorConvenciones.CeldasNumericasSinSpan(
            """<div class="v2-grilla-fila"><div class="v2-grilla-celda">A-001</div><div class="v2-grilla-celda v2-num"><span>340</span></div></div>"""));

    [Fact]
    public void MockupsDelRepo_RespetanLaConvencion()
    {
        // referencia/ son los mockups de dirección aprobados en el brainstorming: no usan base.css.
        var referencia = Path.Combine(RutasRepo.CarpetaMockups, "referencia") + Path.DirectorySeparatorChar;
        var errores = Directory.EnumerateFiles(RutasRepo.CarpetaMockups, "*.html", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(referencia, StringComparison.Ordinal))
            .SelectMany(f => VerificadorConvenciones.CeldasNumericasSinSpan(File.ReadAllText(f))
                .Select(e => $"{Path.GetRelativePath(RutasRepo.CarpetaMockups, f)}: {e}"))
            .ToList();

        Assert.True(errores.Count == 0,
            "Celdas v2-num sin su <span> único (herramientas.md, punto 4):" + Environment.NewLine
            + string.Join(Environment.NewLine, errores));
    }
}
