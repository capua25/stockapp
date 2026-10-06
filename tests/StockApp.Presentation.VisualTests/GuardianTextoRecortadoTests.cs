using Avalonia.Controls;
using Avalonia.Controls.Documents;
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

    [AvaloniaFact]
    public void TextBoxConValorQueNoEntra_SeReporta()
    {
        // Spec §6.5, punto ciego: el valor lo dibuja un TextPresenter, no un TextBlock.
        var recortes = Renderizar(new TextBox { Width = 120, Text = Largo });

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void TextBoxConValorQueEntra_NoSeReporta() =>
        Assert.Empty(Renderizar(new TextBox { Width = 300, Text = "A-001" }));

    [AvaloniaFact]
    public void TextBoxConPlaceholderQueNoEntra_SeReporta()
    {
        var recortes = Renderizar(new TextBox { Width = 120, PlaceholderText = Largo });

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void ContrasenaQueNoEntra_SeReportaSinMostrarElValor()
    {
        const string secreto = "unaContraseniaBastanteLarga123";
        var recortes = Renderizar(new TextBox { Width = 80, PasswordChar = '*', Text = secreto });

        var recorte = Assert.Single(recortes);
        Assert.Contains("(contraseña)", recorte);
        Assert.DoesNotContain(secreto, recorte);
    }

    [AvaloniaFact]
    public void NumericUpDownConValorQueNoEntra_SeReporta()
    {
        // El TextBox interno del NumericUpDown también dibuja con TextPresenter.
        // Se asierta sin separador decimal: depende de la cultura del proceso (la app es es-UY).
        var recortes = Renderizar(new NumericUpDown { Width = 90, Value = 123456789.12m, FormatString = "0.00" });

        Assert.Contains(recortes, r => r.Contains("123456789"));
    }

    [AvaloniaFact]
    public void TextoConInlinesYTextNulo_QueNoEntra_SeReporta()
    {
        var texto = new TextBlock { Width = 60 };
        texto.Inlines = new InlineCollection { new Run(Largo) };

        var recortes = Renderizar(texto);

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void TextoConMaxLinesQueCortaLineas_SeReporta()
    {
        // MaxLines limita también el Measure: sin anularlo, el texto pide lo que tiene.
        var recortes = Renderizar(new TextBlock { Width = 120, TextWrapping = TextWrapping.Wrap, MaxLines = 1, Text = Largo });

        Assert.Contains(Largo, Assert.Single(recortes));
    }

    [AvaloniaFact]
    public void TextoConMaxLinesQueAlcanza_NoSeReporta() =>
        Assert.Empty(Renderizar(new TextBlock { Width = 120, TextWrapping = TextWrapping.Wrap, MaxLines = 3, Text = "Corto" }));

    [AvaloniaFact]
    public void GrillaMasAltaQueLaVentanaSinScrollViewer_SeReporta()
    {
        // Ledger Fase 0, Task 18: el relajamiento del DataGrid seguía vigente en la ventana, así
        // que las filas que caen debajo de los 760 px (inalcanzables: nada scrollea la página)
        // no se reportaban.
        var recortes = RenderizarGrilla(new DataGrid { Height = 1000, Width = 300, Columns = { Columna(150) } }, filas: 40);

        // Lo reporta el UserControl raíz (ClipToBounds, del tamaño de la ventana) antes de llegar
        // a la ventana misma: lo que importa es que ya no se exime por ser viewport.
        Assert.NotEmpty(recortes);
        Assert.All(recortes, r => Assert.Contains("queda fuera de", r));
    }

    [AvaloniaFact]
    public void ScrollViewerQueSeSaleDeLaVentana_SeReporta()
    {
        var recortes = Renderizar(new ScrollViewer
        {
            Height = 1000,
            Width = 300,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel { Children = { new Border { Height = 900 }, new TextBlock { Text = "Debajo de la ventana" } } },
        });

        Assert.Contains(recortes, r => r.Contains("Debajo de la ventana") && r.Contains("la ventana"));
    }

    [AvaloniaFact]
    public void GrillaAltaDentroDeUnScrollViewerQueEntra_NoSeReporta()
    {
        // El ScrollViewer de afuera sí se alcanza entero: la grilla se lee scrolleando la página.
        var grilla = new DataGrid
        {
            Height = 1000,
            Width = 300,
            AutoGenerateColumns = false,
            Columns = { Columna(150) },
            ItemsSource = Enumerable.Range(0, 40).Select(_ => new Fila("A-001")).ToList(),
        };
        var vista = new UserControl
        {
            Classes = { "ui2" },
            Content = new ScrollViewer { Height = 400, Width = 320, HorizontalAlignment = HorizontalAlignment.Left, Content = grilla },
        };
        IReadOnlyList<string> recortes = [];
        RenderizadorVista.RenderizarAPng(vista, null,
            Path.Combine(Directory.CreateTempSubdirectory("guardian-anidado-").FullName, "vista.png"),
            v => recortes = GuardianTextoRecortado.Buscar(v));

        Assert.Empty(recortes);
    }

    [AvaloniaFact]
    public void TextBoxMultilineaDeAltoFijoConValorLargo_NoSeReporta()
    {
        // Deuda de la Fase 1: un TextBox multilínea SÍ se lee scrolleando en vertical (motivo de anulación,
        // token del Reset). Su ScrollViewer relaja el eje vertical; el valor que no entra no es un recorte.
        var valor = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"Línea {i} del motivo"));

        Assert.Empty(Renderizar(new TextBox
        {
            Width = 240, Height = 88, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = valor,
        }));
    }

    [AvaloniaFact]
    public void TextBoxMultilineaSinWrapConUnaLineaMasAnchaQueElControl_SeReporta()
    {
        // El eje horizontal NO se relaja: sin wrap, la línea larga queda cortada a la derecha y nadie la lee.
        var recortes = Renderizar(new TextBox
        {
            Width = 120, Height = 88, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Text = Largo,
        });

        Assert.Contains(recortes, r => r.Contains(Largo));
    }

    [AvaloniaFact]
    public void TextBoxMultilineaQueSeSaleDeLaVentana_SeReporta()
    {
        // Relajar el eje vertical no exime a la caja del TextBox: si ella misma cae fuera de la ventana, se reporta.
        var valor = string.Join("\n", Enumerable.Range(1, 80).Select(i => $"Línea {i}"));
        var recortes = Renderizar(new TextBox
        {
            Width = 240, Height = 1000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Text = valor,
        });

        Assert.Contains(recortes, r => r.Contains("la ventana"));
    }

    [AvaloniaFact]
    public void GrillaQueSeSaleDeLaVentanaSinAncestrosQueRecorten_LaReportaLaVentana()
    {
        // Deuda de la Fase 1: GrillaMasAltaQueLaVentanaSinScrollViewer_SeReporta la reporta el UserControl
        // raíz (ClipToBounds), así que el último chequeo, contra la ventana, nunca corría desde el
        // presentador de filas. Con una raíz que no recorta (un Panel) lo decide ese chequeo.
        var grilla = new DataGrid
        {
            Height = 1000,
            Width = 300,
            AutoGenerateColumns = false,
            Columns = { Columna(150) },
            ItemsSource = Enumerable.Range(0, 40).Select(_ => new Fila("A-001")).ToList(),
        };
        var vista = new StackPanel { Classes = { "ui2" }, HorizontalAlignment = HorizontalAlignment.Left, Children = { grilla } };
        IReadOnlyList<string> recortes = [];
        RenderizadorVista.RenderizarAPng(vista, null,
            Path.Combine(Directory.CreateTempSubdirectory("guardian-ventana-").FullName, "vista.png"),
            v => recortes = GuardianTextoRecortado.Buscar(v));

        Assert.NotEmpty(recortes);
        Assert.All(recortes, r => Assert.EndsWith("queda fuera de la ventana", r));
    }
}
