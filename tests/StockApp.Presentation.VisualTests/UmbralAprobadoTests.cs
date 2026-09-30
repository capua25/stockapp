using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Spec §6.5, umbral por pantalla: subir las teselas densas por encima de lo calibrado exige un
/// registro de aprobación, y llegar al valor de M2 del catálogo exige haber visto en rojo, en esa
/// pantalla, la mutación de relleno pálido. Un número suelto no alcanza.
/// </summary>
public class UmbralAprobadoTests
{
    private const string Fixture = "_fixtures/bloque.html";

    private static UmbralAprobado Aprobado(int teselas, string? mutacionPalida = null, string aprobadoPor = "Martin") =>
        new(teselas, aprobadoPor, "2026-09-30", "8 teselas de tono de texto, revisadas una por una", mutacionPalida);

    [Fact]
    public void SinAprobacion_ElDefaultEsElCalibrado() =>
        Assert.Equal(UmbralComparacion.TeselasDensasPorDefecto, UmbralComparacion.TeselasMaximas(UmbralComparacion.TeselasDensasPorDefecto, null));

    [Fact]
    public void SinAprobacion_UnValorMenorAlCalibradoSeAcepta() =>
        Assert.Equal(2, UmbralComparacion.TeselasMaximas(2, null));

    [Fact]
    public void Aprobado_BajoElTope_ReemplazaAlCalibrado() =>
        Assert.Equal(14, UmbralComparacion.TeselasMaximas(UmbralComparacion.TeselasDensasPorDefecto, Aprobado(14)));

    [Fact]
    public void Aprobado_EnElTopeConLaMutacionPalidaVistaEnRojo_SeAcepta() =>
        Assert.Equal(16, UmbralComparacion.TeselasMaximas(
            UmbralComparacion.TeselasDensasPorDefecto,
            Aprobado(16, mutacionPalida: "Variante=Peligro quitada de la insignia de Stock: 31 teselas, rojo")));

    [AvaloniaFact]
    public void SinAprobacion_TeselasMayoresAlCalibrado_CompararConMockupFalla()
    {
        var ex = Assert.ThrowsAny<Exception>(() => ComparacionVisual.CompararConMockup(
            new Canvas(), null, Fixture, teselasDensasMax: UmbralComparacion.TeselasDensasPorDefecto + 1));

        Assert.Contains("UmbralAprobado", ex.Message);
    }

    [AvaloniaFact]
    public void AprobadoEnElTopeSinLaMutacionPalida_CompararConMockupFalla()
    {
        var ex = Assert.ThrowsAny<Exception>(() => ComparacionVisual.CompararConMockup(
            new Canvas(), null, Fixture, aprobado: Aprobado(UmbralComparacion.TeselasMutacionPalidaCatalogo)));

        Assert.Contains("M2", ex.Message);
    }

    [AvaloniaFact]
    public void AprobadoSinQuienAprueba_CompararConMockupFalla()
    {
        var ex = Assert.ThrowsAny<Exception>(() => ComparacionVisual.CompararConMockup(
            new Canvas(), null, Fixture, aprobado: Aprobado(14, aprobadoPor: " ")));

        Assert.Contains("UmbralAprobado incompleto", ex.Message);
    }

    [AvaloniaFact]
    public void AprobadoConFechaQueNoEsIso_CompararConMockupFalla()
    {
        var ex = Assert.ThrowsAny<Exception>(() => ComparacionVisual.CompararConMockup(
            new Canvas(), null, Fixture, aprobado: Aprobado(14) with { Fecha = "30/09/2026" }));

        Assert.Contains("UmbralAprobado incompleto", ex.Message);
    }
}
