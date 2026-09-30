using Avalonia.Controls;
using Avalonia.Data;
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

    private static IReadOnlyList<string> Renderizar(Control contenido, IReadOnlyCollection<string>? recortesPermitidos = null)
    {
        var vista = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Children = { contenido } };
        IReadOnlyList<string> recortes = [];
        RenderizadorVista.RenderizarAPng(vista, null,
            Path.Combine(Directory.CreateTempSubdirectory("guardian-").FullName, "vista.png"),
            v => recortes = GuardianTextoRecortado.Buscar(v, recortesPermitidos));
        return recortes;
    }

    // En un StackPanel horizontal el TextBlock recibe todo el ancho que pide: su Bounds nunca es
    // menor que el texto. Si se sale, lo recorta un ancestro (breadcrumb de EncabezadoPagina).
    private static Control EnFilaHorizontal(Control texto) =>
        new StackPanel { Orientation = Orientation.Horizontal, Children = { texto } };

    private sealed class Fila(string texto)
    {
        public string Texto { get; } = texto;
    }

    // La grilla v2 scrollea por su cuenta, sin ScrollViewer, y su plantilla recorta
    // (Border#DataGridBorder con ClipToBounds, DataGridV2.axaml).
    private static IReadOnlyList<string> RenderizarGrilla(DataGrid grilla, int filas, string texto = "A-001")
    {
        grilla.AutoGenerateColumns = false;
        grilla.ItemsSource = Enumerable.Range(0, filas).Select(_ => new Fila(texto)).ToList();
        var vista = new UserControl { Classes = { "ui2" }, Content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Children = { grilla } } };
        IReadOnlyList<string> recortes = [];
        RenderizadorVista.RenderizarAPng(vista, null,
            Path.Combine(Directory.CreateTempSubdirectory("guardian-grilla-").FullName, "vista.png"),
            v => recortes = GuardianTextoRecortado.Buscar(v));
        return recortes;
    }

    private static DataGridTextColumn Columna(double ancho) =>
        new() { Header = "X", Binding = new Binding(nameof(Fila.Texto)), Width = new DataGridLength(ancho) };

    [AvaloniaFact]
    public void GrillaConMasFilasQueSuAlto_LaFilaParcialNoSeReporta() =>
        // Se llega scrolleando: el DataGrid es un viewport vertical.
        Assert.Empty(RenderizarGrilla(new DataGrid { Height = 100, Width = 300, Columns = { Columna(150) } }, filas: 10));

    [AvaloniaFact]
    public void GrillaConColumnasMasAnchasQueElla_LaColumnaParcialNoSeReporta() =>
        Assert.Empty(RenderizarGrilla(
            new DataGrid
            {
                Width = 200,
                Height = 200,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Columns = { Columna(150), Columna(150), Columna(150) },
            },
            filas: 2));

    [AvaloniaFact]
    public void CeldaMasAngostaQueSuTexto_SeReportaAunqueLaGrillaSeaViewport()
    {
        var recortes = RenderizarGrilla(new DataGrid { Width = 300, Height = 200, Columns = { Columna(60) } }, filas: 1, texto: Largo);

        Assert.Contains(Largo, Assert.Single(recortes));
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
    public void TextoConWidthPropioMenorQueElTexto_SeReporta()
    {
        // El Width fijo también limita el Measure del TextBlock: medirlo tal cual pide lo que tiene.
        var recortes = Renderizar(new TextBlock { Text = Largo, Width = 60 });

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
    public void TextoConTrimmingQueRecorta_SinPermitirlo_SeReporta()
    {
        var recortes = Renderizar(new Border
        {
            Width = 60,
            Child = new TextBlock { Text = Largo, TextTrimming = TextTrimming.CharacterEllipsis },
        });

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void TextoConTrimmingQueRecorta_PermitidoPorElTest_NoSeReporta() =>
        Assert.Empty(Renderizar(
            new Border { Width = 60, Child = new TextBlock { Text = Largo, TextTrimming = TextTrimming.CharacterEllipsis } },
            recortesPermitidos: [Largo]));

    [AvaloniaFact]
    public void TextoRecortadoPorUnAncestroConClipToBounds_SeReporta()
    {
        var recortes = Renderizar(new Border
        {
            Width = 60,
            ClipToBounds = true,
            Child = EnFilaHorizontal(new TextBlock { Text = Largo }),
        });

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void TextoQueSeSaleDeLaVentana_SeReporta()
    {
        var recortes = Renderizar(EnFilaHorizontal(new TextBlock { Text = Largo, Margin = new(1200, 0, 0, 0) }));

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void TextoQueSigueEnUnScrollViewerDesplazable_NoSeReporta() =>
        // El viewport lo recorta, pero se llega scrolleando: no es texto perdido.
        Assert.Empty(Renderizar(new ScrollViewer
        {
            Width = 60,
            Height = 40,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = Largo },
        }));

    [AvaloniaFact]
    public void TextoDentroDeUnContenedorOculto_NoSeReporta() =>
        // El TextBlock no se oculta él mismo (medirlo daría 0): lo oculta un ancestro, así que
        // nunca se arregló y su Bounds es 0 aunque pida lugar.
        Assert.Empty(Renderizar(new Border { Width = 60, IsVisible = false, Child = new TextBlock { Text = Largo } }));
}
