using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Domain.Enums;
using StockApp.Presentation.Actualizaciones.Views;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Administracion;
using StockApp.Presentation.Views.Catalogo;
using StockApp.Presentation.Views.Dialogs;
using StockApp.Presentation.Views.Documentos;
using StockApp.Presentation.Views.Finanzas;
using StockApp.Presentation.Views.Movimientos;
using StockApp.Presentation.Views.Reportes;
using StockApp.Presentation.Views.Tareas;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// El guardián del sistema nuevo (spec §8.2): cobertura de todas las vistas del ensamblado, piezas del tema viejo
/// prohibidas en v2 (convención 11), botón v2 con variante (un <c>Button</c> sin clase dentro de <c>.ui2</c> no recibe
/// estilo v2), opacidades literales y overlays de modal (convención 25).
///
/// Toda vista nueva entra en <see cref="VistasV2"/> (o en <see cref="VistasFueraDelGuardian"/> con su razón);
/// <see cref="Guardian_CubreTodasLasVistasDelEnsamblado"/> exige que no quede ninguna fuera de las dos listas.
/// </summary>
public class GuardianSistemaV2Tests
{
    /// <summary>
    /// Vistas que no pueden entrar a la lista de vistas v2, con su razon y con quien las custodia en su lugar. Es una
    /// lista de EXCLUSION explicita: el test de cobertura exige que toda vista del ensamblado este en alguna lista o en
    /// esta. No exime un invariante: saca la vista de la cobertura porque <see cref="PatronHelpers.Montar"/> no puede
    /// montarla (Window) o porque no es "una vista de contenido" (chrome de la app).
    /// </summary>
    public static readonly HashSet<Type> VistasFueraDelGuardian = new()
    {
        // Window: PatronHelpers.Montar las meteria como Content de otra Window, y eso tira
        // InvalidOperationException en runtime (verificado empiricamente en la Task 12.3: "The
        // control ... already has a visual parent"). Las custodian el compilador de C# (los
        // x:Name que lee el code-behind dan CS0103 si se rompen, Ruling B-27) y
        // ConfirmacionServiceDialogosConsecutivosTests.
        typeof(ConfirmacionDialog),
        typeof(MensajeDialog),
        typeof(PedirTextoDialog),
        typeof(AltaProductoRapidaDialog),
        typeof(ConfirmacionPreciosDialog),
        typeof(RecalcularStockDialog),
        typeof(ReclasificarTareaDialog),
        // Window host de la app -- tambien queda fuera de Montar por la misma razon (Window).
        // Revisada en la Task 13.1: no tiene ningun literal visual que custodiar.
        typeof(MainWindow),
        // Cromo de la app (sidebar + ContentControl), no una vista de contenido navegable.
        // La custodian ShellMainViewGatesTests (15 casos) y ContrasteV2Tests.
        typeof(ShellMainView),
    };

    /// <summary>
    /// Vistas del sistema v2 (spec 2026-09-28). Las custodian su comparación contra el mockup (VisualTests), sus tests
    /// de UiTests/V2 y este guardián.
    /// </summary>
    public static readonly HashSet<Type> VistasV2 = new()
    {
        typeof(LoginView),
        typeof(AdjuntosPanelView),
        typeof(CalendarioPagosView),
        typeof(ControlPoaView),
        typeof(ResetAdminView),
        typeof(BloqueoLicenciaView),
        typeof(ActualizacionBannerView),
        typeof(ActualizacionModalView),
        typeof(ActualizacionBloqueoView),
        typeof(ActualizacionAvisoCriticoView),
        typeof(InicioView),
        typeof(ProductoListView),
        typeof(ProductoFormView),
        typeof(MovimientoFormControl),
        typeof(EntradaRegistroView),
        typeof(SalidaRegistroView),
        typeof(MovimientoHistorialView),
        typeof(IngresoPorFacturaView),
        typeof(IngresosView),
        typeof(IngresoFormView),
        typeof(LibroCajaView),
        typeof(GastosView),
        typeof(GastoFormView),
        typeof(PagosGastoView),
        typeof(ReportesView),
        typeof(MarcoReporte),
        typeof(ValorizacionView),
        typeof(StockCategoriaView),
        typeof(MasMovidosView),
        typeof(HistorialPorProductoView),
        typeof(AuditoriaLogView),
        typeof(ReporteProyectosView),
        typeof(CatalogosView),
        typeof(CategoriaFormView),
        typeof(ProveedorFormView),
        typeof(UnidadMedidaFormView),
        typeof(ZonaFormView),
        typeof(DimensionTematicaFormView),
        typeof(OrganismoResponsableFormView),
        typeof(OrigenFinanciamientoFormView),
        typeof(FuenteFinanciamientoFormView),
        typeof(RubroGastoFormView),
        typeof(LineaPoaFormView),
        typeof(CatalogoListadoView),
        typeof(UsuariosAdminView),
        typeof(MantenimientoView),
        typeof(TareaListView),
        typeof(TareaFormView),
        typeof(ClasificacionTareaPanelView),
        typeof(DocumentoListView),
        typeof(DocumentoFormView),
        typeof(AdjuntosDocumentoPanelView),
        typeof(AccesoLimitadoView),
    };

    /// <summary>
    /// Enumera por reflexion todos los Control publicos, no abstractos, de los namespaces de vistas del ensamblado
    /// StockApp.Presentation y exige que cada uno este en <see cref="VistasV2"/>
    /// o <see cref="VistasFueraDelGuardian"/>: agregar una vista
    /// y olvidarla en las listas dejaria invariantes sin correr.
    ///
    /// El predicado de namespace excluye deliberadamente StockApp.Presentation.Controls: no son vistas, son piezas del
    /// catalogo de componentes.
    /// </summary>
    [AvaloniaFact]
    public void Guardian_CubreTodasLasVistasDelEnsamblado()
    {
        var todas = typeof(ViewLocator).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true } && typeof(Control).IsAssignableFrom(t))
            .Where(t => t.Namespace is not null
                        && (t.Namespace.StartsWith("StockApp.Presentation.Views", StringComparison.Ordinal)
                            || t.Namespace == "StockApp.Presentation.Actualizaciones.Views"))
            .ToList();

        var cubiertas = VistasV2
            .Concat(VistasFueraDelGuardian)
            .ToHashSet();

        var huerfanas = todas.Where(t => !cubiertas.Contains(t)).ToList();
        Assert.True(huerfanas.Count == 0,
            "Vista(s) sin ninguna lista del guardian ni exclusion documentada: "
            + string.Join(", ", huerfanas.Select(t => t.Name)));
    }

    [Fact]
    public void Listas_SonDisjuntas()
    {
        var listas = new[]
        {
            ("VistasV2", (IEnumerable<Type>)VistasV2),
            ("VistasFueraDelGuardian", VistasFueraDelGuardian),
        };

        var repetidos = listas.SelectMany(l => l.Item2.Select(t => (Lista: l.Item1, Tipo: t)))
            .GroupBy(x => x.Tipo)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Name} ({string.Join(" y ", g.Select(x => x.Lista))})")
            .ToList();

        Assert.True(repetidos.Count == 0, "Vista(s) en más de una lista del guardián: " + string.Join(", ", repetidos));
    }

    /// <summary>
    /// Opacidades literales en las vistas v2 y en el cromo del shell (sidebar v2): ShellMainView está fuera de
    /// la lista de vistas, pero se puede montar.
    ///
    /// Cobertura PARCIAL, por diseño: PatronHelpers.Montar no asigna DataContext, así que lo que solo existe con un
    /// ViewModel (plantillas de celda de DataGrid, ItemTemplates, contenido tras bindings) no se realiza y no se
    /// mide. Para ShellMainView, que sin VM no dibuja el sidebar completo, lo cubre aparte
    /// ShellV2ConViewModel_NoTieneOpacidadesLiterales. Un literal dentro de una plantilla de otra vista migrada
    /// queda fuera de este chequeo.
    /// </summary>
    public static TheoryData<Type> VistasV2Montables =>
        new(VistasV2.Append(typeof(ShellMainView)).OrderBy(t => t.Name));

    [AvaloniaTheory]
    [MemberData(nameof(VistasV2Montables))]
    public void VistaV2_NoTieneOpacidadesLiterales(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var literales = PatronHelpers.OpacidadesLiteralesDe(vista);

        Assert.True(literales.Count == 0,
            $"{tipoVista.Name} (v2) tiene {literales.Count} control(es) con Opacity literal: "
            + string.Join(", ", literales.Select(c => c.GetType().Name)));
    }

    /// <summary>
    /// Complemento de <see cref="VistaV2_NoTieneOpacidadesLiterales"/> para el shell: con un ShellMainViewModel real
    /// (fakes de sesión/navegación, grupos expandidos) se realiza el sidebar completo y sus ItemTemplates.
    /// </summary>
    [AvaloniaFact]
    public void ShellV2ConViewModel_NoTieneOpacidadesLiterales()
    {
        var vm = new ShellMainViewModel(
            new SesionFake(RolUsuario.Admin, Array.Empty<string>()),
            new NavigationServiceFake(),
            new InfoAppFake(),
            new ConfirmacionServiceFake(),
            new AuthServiceFake(),
            new PreferenciasSidebarFake());
        foreach (var grupo in vm.Grupos)
            grupo.EstaExpandido = true;

        var vista = new ShellMainView();
        var window = new Window { Width = 1200, Height = 900, DataContext = vm, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var literales = PatronHelpers.OpacidadesLiteralesDe(vista);

        Assert.True(literales.Count == 0,
            $"ShellMainView (v2, con ViewModel) tiene {literales.Count} control(es) con Opacity literal: "
            + string.Join(", ", literales.Select(c => c.GetType().Name)));
    }

    private static readonly string[] ClasesDeBotonV2 = { "primario", "secundario", "ghost", "peligro" };

    /// <summary>
    /// Compensación de spec 6.5: el estilo de los botones v2 centra el texto con TextAlignment, y un botón estirado
    /// tiene que estirar también su contenido; con el contenido centrado por HorizontalContentAlignment el origen del
    /// texto se redondea y queda corrido respecto del mockup (medido: hasta 0,8 px, y el Reset con error llegaba al
    /// máximo de teselas). No se pone en el estilo común porque con contenido no textual (ícono, ícono y texto)
    /// estirarlo lo alinea a la izquierda. Los diálogos son Window y no entran en VistasV2Montables; hoy no tienen
    /// botones estirados. El sidebar del shell (lo que solo existe con ViewModel) tampoco: no tiene botones v2 estirados.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(VistasV2Montables))]
    public void VistaV2_BotonEstiradoEstiraSuContenido(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);

        var sinEstirar = vista.GetVisualDescendants().OfType<Button>()
            .Where(b => ClasesDeBotonV2.Any(b.Classes.Contains))
            .Where(b => b.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Stretch)
            .Where(b => b.HorizontalContentAlignment != Avalonia.Layout.HorizontalAlignment.Stretch)
            .Select(b => b.Name ?? b.Content?.ToString() ?? "(sin nombre)")
            .ToList();

        Assert.True(sinEstirar.Count == 0,
            $"{tipoVista.Name} tiene botón(es) v2 estirado(s) sin HorizontalContentAlignment=\"Stretch\": "
            + string.Join(", ", sinEstirar));
    }

    /// <summary>
    /// Custodia el Setter TextBlock.TextAlignment=Center del estilo común de botones v2 (spec 6.5): el TextBlock que
    /// el ContentPresenter crea para un Content string tiene que centrar el texto, como lo centra Chromium.
    /// </summary>
    [AvaloniaFact]
    public void EstiloBotonV2_CentraElTextoConTextAlignment()
    {
        var vista = PatronHelpers.Montar(typeof(LoginView));
        var entrar = vista.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BotonEntrar");

        var texto = entrar.GetVisualDescendants().OfType<TextBlock>().Single();

        Assert.Equal("Entrar", texto.Text);
        Assert.Equal(Avalonia.Media.TextAlignment.Center, texto.TextAlignment);
    }

    /// <summary>
    /// El TextAlignment Center del estilo de botones v2 es heredable y llegaba al ToolTip y al Flyout del botón (el
    /// tooltip estándar de bloqueo del guardado salía envuelto y centrado). ControlesV2.axaml lo corta con estilos
    /// ToolTip y FlyoutPresenter en Start; esto lo custodia con los popups abiertos.
    /// </summary>
    [AvaloniaFact]
    public void BotonV2_ToolTipYFlyoutNoHeredanElCentrado()
    {
        var textoTip = new TextBlock { Text = "Corregí los campos marcados en rojo para continuar.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var textoFlyout = new TextBlock { Text = "Texto de un flyout de varias palabras", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var flyout = new Flyout { Content = textoFlyout };
        var boton = new Button { Content = "Guardar", Flyout = flyout };
        boton.Classes.Add("primario");
        ToolTip.SetTip(boton, textoTip);
        var raiz = new StackPanel { Children = { boton } };
        raiz.Classes.Add("ui2");
        var window = new Window { Width = 600, Height = 400, Content = raiz };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            ToolTip.SetIsOpen(boton, true);
            Dispatcher.UIThread.RunJobs();
            flyout.ShowAt(boton);
            Dispatcher.UIThread.RunJobs();

            var textoDelBoton = boton.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(Avalonia.Media.TextAlignment.Center, textoDelBoton.TextAlignment);
            Assert.NotNull(TopLevel.GetTopLevel(textoTip));
            Assert.Equal(Avalonia.Media.TextAlignment.Start, textoTip.TextAlignment);
            Assert.NotNull(TopLevel.GetTopLevel(textoFlyout));
            Assert.Equal(Avalonia.Media.TextAlignment.Start, textoFlyout.TextAlignment);
        }
        finally
        {
            flyout.Hide();
            ToolTip.SetIsOpen(boton, false);
            window.Close();
        }
    }

    private const string XamlFixtureOpacidades = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="300" Height="200">
            <StackPanel>
                <StackPanel.Styles>
                    <Style Selector="Border.por-estilo">
                        <Setter Property="Opacity" Value="0.28" />
                    </Style>
                </StackPanel.Styles>
                <Border x:Name="Literal" Width="20" Height="20" Opacity="0.5" />
                <Border x:Name="PorEstilo" Classes="por-estilo" Width="20" Height="20" />
                <Border x:Name="Normal" Width="20" Height="20" />
            </StackPanel>
        </Window>
        """;

    [AvaloniaFact]
    public void OpacidadesLiteralesDe_DevuelveSoloElLiteralYNoElQueVieneDeUnStyle()
    {
        // Mismo camino que una vista real: XAML cargado en runtime, atributo vs Setter de Style.
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(XamlFixtureOpacidades, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var literales = PatronHelpers.OpacidadesLiteralesDe((Control)window.Content!);

        var unico = Assert.Single(literales);
        Assert.Equal("Literal", unico.Name);
    }

    /// <summary>
    /// Los diálogos son Window: PatronHelpers.Montar no puede montarlos (ver VistasFueraDelGuardian). Se lee
    /// su .axaml: un atributo Opacity="…" en un elemento es un literal; un Setter (Property="Opacity") no.
    /// </summary>
    private static readonly Regex OpacidadLiteralEnXaml = new(@"<[A-Za-z][\w:.]*\b[^>]*\sOpacity\s*=\s*""", RegexOptions.Compiled);

    public static TheoryData<string> DialogosV2 => new()
    {
        "StockApp.Presentation/Views/Dialogs/ConfirmacionDialog.axaml",
        "StockApp.Presentation/Views/Dialogs/MensajeDialog.axaml",
        "StockApp.Presentation/Views/Dialogs/PedirTextoDialog.axaml",
        "StockApp.Presentation/Views/Dialogs/RecalcularStockDialog.axaml",
        "StockApp.Presentation/Views/Dialogs/AltaProductoRapidaDialog.axaml",
        "StockApp.Presentation/Views/Dialogs/ConfirmacionPreciosDialog.axaml",
        "StockApp.Presentation/Views/Tareas/ReclasificarTareaDialog.axaml",
    };

    [Theory]
    [MemberData(nameof(DialogosV2))]
    public void DialogoV2_NoTieneOpacidadLiteralEnSuXaml(string rutaDesdeSrc)
    {
        var xaml = LeerDesdeSrc(rutaDesdeSrc);

        Assert.DoesNotMatch(OpacidadLiteralEnXaml, xaml);
    }

    [Fact]
    public void DetectorDeOpacidadLiteral_VeUnAtributoYNoUnSetter()
    {
        // Control positivo del detector: sin esto, un regex roto deja verde DialogoV2_NoTieneOpacidadLiteralEnSuXaml.
        Assert.Matches(OpacidadLiteralEnXaml, """<Border x:Name="Fondo" Opacity="0.5" />""");
        Assert.Matches(OpacidadLiteralEnXaml, "<TextBlock\n    Text=\"x\"\n    Opacity=\"0.6\" />");
        Assert.DoesNotMatch(OpacidadLiteralEnXaml, """<Setter Property="Opacity" Value="0.28" />""");
    }

    private static readonly Regex ConversorOControlViejo =
        new(@"SignoNegativoBrushConverter|ActivoOpacidadConverter|<c:BadgeEstado\b|<c:HeaderVista\b|<c:EstadoVacio\b", RegexOptions.Compiled);

    private static readonly Regex ClaseDelTemaViejo =
        new(@"Classes\s*=\s*""[^""]*(?<![\w-])(primary|secondary|card|seccion|caption|micro|body|titulo-vista)(?![\w-])[^""]*""", RegexOptions.Compiled);

    private static readonly Regex ClaveEnLlave = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);

    private static readonly string[] ArchivosDelTemaViejo =
    {
        "StockApp.Presentation/Themes/Tokens.axaml",
        "StockApp.Presentation/Themes/Typography.axaml",
        "StockApp.Presentation/Themes/DataGrid.axaml",
        "StockApp.Presentation/Controls/Componentes.axaml",
    };

    private static readonly Lazy<string[]> ClavesDelTemaViejo = new(() =>
        ArchivosDelTemaViejo
            .SelectMany(a => ClaveEnLlave.Matches(LeerDesdeSrc(a)).Select(m => m.Groups[1].Value))
            .Where(k => !k.StartsWith("{x:Type", StringComparison.Ordinal))
            .Distinct()
            .ToArray());

    /// <summary>
    /// Cada pieza del tema viejo que aparece en el texto: conversores y controles (<c>SignoNegativoBrushConverter</c>,
    /// <c>c:BadgeEstado</c>, <c>c:HeaderVista</c>, <c>c:EstadoVacio</c>), recursos con la clave de un token del tema
    /// viejo (los v2 llevan el prefijo V2 y no coinciden) y clases de estilo del tema viejo (<c>primary</c>,
    /// <c>card</c>…; <c>ghost</c> no entra: es de los dos temas).
    /// </summary>
    private static List<string> PiezasViejasEn(string texto, IEnumerable<string> claves)
    {
        var piezas = ConversorOControlViejo.Matches(texto).Select(m => m.Value).ToList();
        var alternativa = string.Join("|", claves.Select(Regex.Escape));
        if (alternativa.Length > 0)
            piezas.AddRange(Regex.Matches(texto, $@"(Dynamic|Static)Resource\s+(?:{alternativa})\b").Select(m => m.Value));
        piezas.AddRange(ClaseDelTemaViejo.Matches(texto).Select(m => m.Value));
        return piezas;
    }

    /// <summary>Ruta desde src del .axaml de una vista: el namespace StockApp.Presentation.X.Y es la carpeta X/Y.</summary>
    private static string RutaAxaml(Type vista) =>
        "StockApp.Presentation/" + vista.Namespace!["StockApp.Presentation.".Length..].Replace('.', '/') + "/" + vista.Name + ".axaml";

    /// <summary>Los .axaml del sistema v2: las vistas, el cromo del shell y los diálogos (que son Window y no se montan).</summary>
    public static TheoryData<string> AxamlV2 => new(
        VistasV2.Select(RutaAxaml)
            .Append("StockApp.Presentation/Views/ShellMainView.axaml")
            .Concat(DialogosV2.Select(d => d.Data))
            .OrderBy(r => r, StringComparer.Ordinal));

    /// <summary>
    /// Una vista v2 no usa los conversores, los controles, los tokens ni las clases del tema viejo (convención 11): se
    /// lee el TEXTO del .axaml, así cuenta también lo que vive en plantillas de celda, que el montaje sin ViewModel no
    /// realiza.
    /// </summary>
    [Theory]
    [MemberData(nameof(AxamlV2))]
    public void VistaV2_NoUsaPiezasDelTemaViejo(string rutaDesdeSrc)
    {
        var piezas = PiezasViejasEn(LeerDesdeSrc(rutaDesdeSrc), ClavesDelTemaViejo.Value);

        Assert.True(piezas.Count == 0, $"{rutaDesdeSrc} usa piezas del tema viejo: {string.Join(", ", piezas)}");
    }

    [Fact]
    public void DetectorDePiezasViejas_VeConversorControlTokenYClase()
    {
        string[] claves = { "FondoBrush", "Espacio3" };

        Assert.NotEmpty(PiezasViejasEn("""<TextBlock Foreground="{Binding X, Converter={x:Static conv:SignoNegativoBrushConverter.Instance}}" />""", claves));
        Assert.NotEmpty(PiezasViejasEn("""<c:BadgeEstado Texto="x" />""", claves));
        Assert.NotEmpty(PiezasViejasEn("""<Border Background="{DynamicResource FondoBrush}" />""", claves));
        Assert.NotEmpty(PiezasViejasEn("""<StackPanel Spacing="{StaticResource Espacio3}" />""", claves));
        Assert.NotEmpty(PiezasViejasEn("""<Button Classes="primary" Content="x" />""", claves));
        Assert.NotEmpty(PiezasViejasEn("""<Border Classes="card x" />""", claves));

        Assert.Empty(PiezasViejasEn("""<Border Background="{DynamicResource V2FondoBrush}" />""", claves));
        Assert.Empty(PiezasViejasEn("""<Button Classes="primario" Content="x" />""", claves));
        Assert.Empty(PiezasViejasEn("""<Button Classes="ghost" Content="x" />""", claves));
        Assert.Empty(PiezasViejasEn("""<Border Classes="v2-card" />""", claves));
        Assert.Empty(PiezasViejasEn("""<v2:Insignia Texto="x" />""", claves));
    }

    private static readonly Regex BotonElemento = new(@"<Button\s[^>]*>", RegexOptions.Compiled);

    private static readonly Regex BotonConVariante =
        new(@"Theme\s*=\s*""|Classes\s*=\s*""[^""]*(?<![\w-])(primario|secundario|ghost|peligro)(?![\w-])", RegexOptions.Compiled);

    /// <summary>Cada <c>&lt;Button …&gt;</c> del texto que no lleva un Theme ni una clase de variante v2.</summary>
    private static List<string> BotonesSinVarianteEn(string texto) =>
        BotonElemento.Matches(texto).Select(m => m.Value).Where(b => !BotonConVariante.IsMatch(b)).ToList();

    /// <summary>
    /// Un <c>Button</c> sin clase dentro de <c>.ui2</c> no recibe ningún estilo v2 y cae al gris de Fluent: todo botón v2
    /// lleva <c>primario</c>, <c>secundario</c>, <c>ghost</c>, <c>peligro</c> o un <c>Theme</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(AxamlV2))]
    public void VistaV2_TodoBotonTieneVarianteOTema(string rutaDesdeSrc)
    {
        var botones = BotonesSinVarianteEn(LeerDesdeSrc(rutaDesdeSrc));

        Assert.True(botones.Count == 0, $"{rutaDesdeSrc} tiene Button sin variante ni Theme: {string.Join(" | ", botones)}");
    }

    [Fact]
    public void DetectorDeBotonSinVariante_VeElBotonDesnudoYNoElElementoDePropiedad()
    {
        Assert.Single(BotonesSinVarianteEn("""<Button Content="x" />"""));
        Assert.Single(BotonesSinVarianteEn("""<Button Classes="primary" Content="x" />"""));
        Assert.Single(BotonesSinVarianteEn("<Button\n  Content=\"x\" />"));

        Assert.Empty(BotonesSinVarianteEn("""<Button.IsVisible>"""));
        Assert.Empty(BotonesSinVarianteEn("""<Button Classes="secundario" />"""));
        Assert.Empty(BotonesSinVarianteEn("""<Button Classes="ghost icono" />"""));
        Assert.Empty(BotonesSinVarianteEn("""<Button Theme="{StaticResource V2TemaEnlace}" />"""));
    }


    /// <summary>
    /// Convención 25: un modal de pantalla es una Window con servicio, nunca una capa semitransparente sobre la vista
    /// ligada a un flag Mostrando* del ViewModel (no atrapa el foco ni responde a Escape y Enter). Se barre el TEXTO de
    /// todos los .axaml de Views, no solo los v2: un overlay nuevo en una vista vieja también se rechaza.
    /// </summary>
    private static readonly Regex OverlayDeModal =
        new(@"#(?!FF)[0-9A-Fa-f]{2}000000|\{(Compiled|Reflection)?Binding\s+(Path\s*=\s*)?!?Mostrando|Path=""Mostrando",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void NingunaVista_TieneOverlaysDeModal()
    {
        var views = DirectorioViews();
        var ofensores = OverlaysEn(views);

        Assert.True(Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories).Count() > 40,
            "El barrido no encontró las vistas: el guardián no custodiaría nada.");
        Assert.True(ofensores.Count == 0, "Overlay de modal en: " + string.Join(", ", ofensores)
            + ". Un modal de pantalla es una Window con servicio (convención 25 de docs/ui-v2/herramientas.md).");
    }

    [Fact]
    public void DetectorDeOverlays_VeElFondoYElFlagYNoUnBindingComun()
    {
        Assert.Matches(OverlayDeModal, """<Border IsVisible="{Binding MostrandoAltaProducto}" Background="#AA000000">""");
        Assert.Matches(OverlayDeModal, """<Border Background="#aa000000" />""");
        Assert.Matches(OverlayDeModal, """<Border IsVisible="{Binding !MostrandoConfirmacionPrecios}">""");
        Assert.Matches(OverlayDeModal, """<Binding Path="MostrandoAlgo" />""");
        Assert.Matches(OverlayDeModal, """<Border Background="#80000000" />""");
        Assert.Matches(OverlayDeModal, """<Border Background="#99000000" />""");
        Assert.Matches(OverlayDeModal, """<Border IsVisible="{Binding Path=MostrandoX}">""");
        Assert.Matches(OverlayDeModal, """<Border IsVisible="{CompiledBinding MostrandoX}">""");
        Assert.DoesNotMatch(OverlayDeModal, """<Border Background="#FF000000" />""");
        Assert.DoesNotMatch(OverlayDeModal, """<Border IsVisible="{Binding GuardadoExitoso}">""");
        Assert.DoesNotMatch(OverlayDeModal, """<Border Background="{DynamicResource V2FondoBrush}" />""");
    }

    private static List<string> OverlaysEn(string directorio) =>
        Directory.EnumerateFiles(directorio, "*.axaml", SearchOption.AllDirectories)
            .Where(a => OverlayDeModal.IsMatch(File.ReadAllText(a)))
            .Select(a => Path.GetFileName(a)!)
            .ToList();

    private static string DirectorioViews([CallerFilePath] string archivoDeEsteTest = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivoDeEsteTest)!, "..", "..", "src", "StockApp.Presentation", "Views"));

    private static string LeerDesdeSrc(string rutaDesdeSrc, [CallerFilePath] string archivoDeEsteTest = "")
    {
        var ruta = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivoDeEsteTest)!, "..", "..", "src", rutaDesdeSrc));
        Assert.True(File.Exists(ruta), $"No existe {ruta}");
        return File.ReadAllText(ruta);
    }
}
