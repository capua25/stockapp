using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using StockApp.Presentation.VisualTests.Comparacion;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// El guardián semántico de la comparación visual: un glifo que falta ("CÓDIG" por "CÓDIGO",
/// Task 17) pesa lo mismo que el ruido de antialiasing y ninguna métrica de píxeles lo ve.
/// </summary>
public class GuardianTextoRecortadoTests
{
    private const string Largo = "Un texto bastante más largo que su contenedor";

    private static IReadOnlyList<string> Renderizar(Control contenido)
    {
        var vista = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Children = { contenido } };
        IReadOnlyList<string> recortes = [];
        RenderizadorVista.RenderizarAPng(vista, null,
            Path.Combine(Directory.CreateTempSubdirectory("guardian-").FullName, "vista.png"),
            v => recortes = GuardianTextoRecortado.Buscar(v));
        return recortes;
    }

    [AvaloniaFact]
    public void TextoQueEntra_NoSeReporta() =>
        Assert.Empty(Renderizar(new TextBlock { Text = "Código" }));

    [AvaloniaFact]
    public void TextoSinWrapMasAnchoQueSuLugar_SeReportaConSuTexto()
    {
        var recortes = Renderizar(new Border { Width = 60, Child = new TextBlock { Text = Largo } });

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void TextoConMargenYPadding_NoSeConfundeConUnRecorte() =>
        Assert.Empty(Renderizar(new TextBlock { Text = "Stock", Margin = new(12), Padding = new(8, 4) }));

    [AvaloniaFact]
    public void TextoConWrapPartidoEnLineasQueNoEntranEnElAlto_SeReporta()
    {
        var recortes = Renderizar(new Border
        {
            Width = 80,
            Height = 20,
            Child = new TextBlock { Text = Largo, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top },
        });

        Assert.Single(recortes);
    }

    [AvaloniaFact]
    public void TextoConWrapQueEntraEnVariasLineas_NoSeReporta() =>
        Assert.Empty(Renderizar(new Border { Width = 80, Child = new TextBlock { Text = Largo, TextWrapping = TextWrapping.Wrap } }));

    [AvaloniaFact]
    public void TextoConTrimming_EsRecortableDeclaradoYNoSeReporta() =>
        Assert.Empty(Renderizar(new Border
        {
            Width = 60,
            Child = new TextBlock { Text = Largo, TextTrimming = TextTrimming.CharacterEllipsis },
        }));

    [AvaloniaFact]
    public void TextoDentroDeUnContenedorOculto_NoSeReporta() =>
        // El TextBlock no se oculta él mismo (medirlo daría 0): lo oculta un ancestro, así que
        // nunca se arregló y su Bounds es 0 aunque pida lugar.
        Assert.Empty(Renderizar(new Border { Width = 60, IsVisible = false, Child = new TextBlock { Text = Largo } }));
}
