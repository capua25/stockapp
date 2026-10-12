using Avalonia.Headless.XUnit;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Panel de adjuntos vacío en la card de 760 sobre fondo app (el panel con un adjunto se compara dentro de
/// Ingreso por factura guardado).</summary>
[Trait("Categoria", "ComparacionVisual")]
public class AdjuntosComparacionTests
{
    [AvaloniaFact]
    public void AdjuntosVacio_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(DatosAdjuntos.EnTarjeta(DatosAdjuntos.VmVacio()), null, "finanzas/adjuntos-vacio.html");

    [AvaloniaFact]
    public void AdjuntosVacio_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(DatosAdjuntos.EnTarjeta(DatosAdjuntos.VmVacio()), null, "finanzas/adjuntos-vacio.html",
            tamano: TamanoLienzo.Municipio);
}
