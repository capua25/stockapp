using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Ledger Fase 0, Task 12: el tramo actual del breadcrumb no tenía límite y un nombre largo se
/// salía de la card. Con métricas reales de Inter (Skia), baja de línea en vez de recortarse.
/// </summary>
public class EncabezadoPaginaBreadcrumbTests
{
    private const string Largo = "Pintura látex blanca 4 L para exteriores, color blanco tiza, marca de prueba";

    [AvaloniaFact]
    public void BreadcrumbLargo_EnUnaCardAngosta_BajaDeLineaSinRecortar()
    {
        var encabezado = new EncabezadoPagina { Titulo = "Editar producto", BreadcrumbOrigen = "Productos", BreadcrumbActual = Largo };
        var vista = new UserControl
        {
            Classes = { "ui2" },
            Content = new Border { Width = 400, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Child = encabezado },
        };
        IReadOnlyList<string> recortes = [];
        double altoActual = 0;
        RenderizadorVista.RenderizarAPng(vista, null,
            Path.Combine(Directory.CreateTempSubdirectory("breadcrumb-").FullName, "vista.png"),
            v =>
            {
                recortes = GuardianTextoRecortado.Buscar(v);
                altoActual = v.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_BreadcrumbActual").Bounds.Height;
            });

        Assert.Empty(recortes);
        Assert.True(altoActual > 16, $"El tramo actual tenía que ocupar más de una línea de 16 px y mide {altoActual}.");
    }
}
