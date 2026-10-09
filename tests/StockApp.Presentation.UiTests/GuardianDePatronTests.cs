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
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardian de bloque para el catalogo de patrones P0-P7 (Task 6.0 del plan de Fase B,
/// 2026-08-19). 42 de las 55 vistas restantes de la Fase B no tienen ni un test de UI propio;
/// escribir uno por vista seria un plan mas grande que el refactor. El compilador de bindings
/// (AvaloniaUseCompiledBindingsByDefault=true) ya cubre los bindings — lo que NO ve es "esta
/// vista tiene un HeaderVista con el titulo correcto", "su margen exterior es el estandar" y
/// "no quedo ningun Opacity literal decorativo". Eso es lo que este guardian mide, sobre N
/// vistas de golpe, con un solo [AvaloniaTheory] por invariante.
///
/// Las tandas 7-12 agregan sus propias filas a las mismas InlineData en vez de escribir un
/// guardian nuevo cada vez.
///
/// Titulo/Eyebrow en null: para las vistas cuyo HeaderVista toma el titulo de un binding a VM,
/// PatronHelpers.Montar no asigna ViewModel, asi que el texto no se puede verificar literal en este
/// banco de pruebas. Se deja en null y el test solo exige que el HeaderVista exista.
/// </summary>
public class GuardianDePatronTests
{
    [AvaloniaTheory]
    [InlineData(typeof(IngresoPorFacturaView), "Ingreso de stock por factura", "MOVIMIENTOS")]
    [InlineData(typeof(CategoriaListView), "Categorías", "CATÁLOGO")]
    [InlineData(typeof(ProveedorListView), "Proveedores", "CATÁLOGO")]
    [InlineData(typeof(UnidadMedidaListView), "Unidades de medida", "CATÁLOGO")]
    [InlineData(typeof(CategoriaFormView), null, "CATÁLOGO")]
    [InlineData(typeof(ProveedorFormView), null, null)]
    [InlineData(typeof(UnidadMedidaFormView), null, "CATÁLOGO")]
    [InlineData(typeof(ZonaFormView), null, "TAREAS")]
    [InlineData(typeof(DimensionTematicaFormView), null, "TAREAS")]
    [InlineData(typeof(OrganismoResponsableFormView), null, "TAREAS")]
    [InlineData(typeof(OrigenFinanciamientoFormView), null, "TAREAS")]
    [InlineData(typeof(MaestrosFinanzasView), "Maestros de finanzas", "FINANZAS")]
    [InlineData(typeof(CatalogosTareaView), "Catálogos de tareas", "TAREAS")]
    [InlineData(typeof(ImportacionView), "Importar planillas", "FINANZAS")]
    [InlineData(typeof(FuenteFinanciamientoFormView), null, "FINANZAS")]
    [InlineData(typeof(RubroGastoFormView), null, "FINANZAS")]
    [InlineData(typeof(IngresoFormView), null, "FINANZAS")]
    [InlineData(typeof(LineaPoaFormView), null, "FINANZAS")]
    [InlineData(typeof(GastoFormView), null, "FINANZAS")]
    [InlineData(typeof(GastosView), "Gastos y facturas", "FINANZAS")]
    [InlineData(typeof(IngresosView), "Ingresos de caja", "FINANZAS")]
    [InlineData(typeof(ControlPoaView), "Control POA", "FINANZAS")]
    [InlineData(typeof(LibroCajaView), "Libro caja", "FINANZAS")]
    [InlineData(typeof(CalendarioPagosView), "Calendario de pagos", "FINANZAS")]
    [InlineData(typeof(PagosGastoView), "Pagos de la factura", "FINANZAS")]
    [InlineData(typeof(DocumentoListView), "Documentos administrativos", "DOCUMENTOS")]
    [InlineData(typeof(DocumentoFormView), "Nuevo documento", "DOCUMENTOS")]
    [InlineData(typeof(TareaListView), "Tareas", "TAREAS")]
    [InlineData(typeof(TareaFormView), "Nueva tarea", "TAREAS")]
    [InlineData(typeof(ValorizacionView), "Valorización de inventario", "REPORTES")]
    [InlineData(typeof(StockCategoriaView), "Stock por categoría", "REPORTES")]
    [InlineData(typeof(MasMovidosView), "Productos más movidos", "REPORTES")]
    [InlineData(typeof(HistorialPorProductoView), "Historial por producto", "REPORTES")]
    [InlineData(typeof(AuditoriaLogView), "Auditoría", "REPORTES")]
    [InlineData(typeof(ReporteTareasView), "Estadística de tareas", "REPORTES")]
    [InlineData(typeof(UsuariosAdminView), "Administración de usuarios", "ADMINISTRACIÓN")]
    [InlineData(typeof(MantenimientoView), "Mantenimiento", "ADMINISTRACIÓN")]
    public void Vista_TieneHeaderVistaConElTituloEsperado(Type tipoVista, string? titulo, string? eyebrow)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var header = PatronHelpers.HeaderDe(vista);

        Assert.True(header is not null,
            $"{tipoVista.Name} no tiene un HeaderVista en su arbol visual.");

        if (titulo is not null)
            Assert.Equal(titulo, header!.Titulo);
        if (eyebrow is not null)
            Assert.Equal(eyebrow, header!.Eyebrow);
    }

    public static readonly TheoryData<Type> VistasDeLaTanda = new()
    {
        typeof(IngresoPorFacturaView),
        typeof(CategoriaListView),
        typeof(ProveedorListView),
        typeof(UnidadMedidaListView),
        typeof(CategoriaFormView),
        typeof(ProveedorFormView),
        typeof(UnidadMedidaFormView),
        typeof(ZonaFormView),
        typeof(DimensionTematicaFormView),
        typeof(OrganismoResponsableFormView),
        typeof(OrigenFinanciamientoFormView),
        typeof(MaestrosFinanzasView),
        typeof(CatalogosTareaView),
        typeof(ImportacionView),
        typeof(FuenteFinanciamientoFormView),
        typeof(RubroGastoFormView),
        typeof(IngresoFormView),
        typeof(LineaPoaFormView),
        typeof(GastoFormView),
        typeof(GastosView),
        typeof(IngresosView),
        typeof(ControlPoaView),
        typeof(LibroCajaView),
        typeof(CalendarioPagosView),
        typeof(PagosGastoView),
        typeof(DocumentoListView),
        typeof(DocumentoFormView),
        typeof(TareaListView),
        typeof(TareaFormView),
        typeof(ValorizacionView),
        typeof(StockCategoriaView),
        typeof(MasMovidosView),
        typeof(HistorialPorProductoView),
        typeof(AuditoriaLogView),
        typeof(ReporteTareasView),
        typeof(UsuariosAdminView),
        typeof(MantenimientoView),
    };

    /// <summary>
    /// Vistas que no aportan cromo de vista propio: o estan embebidas en otra (P2-emb, P1-emb, P5,
    /// Task 8.1 de la Fase B; P5-ovl, Ruling B-34, tanda 12), o hostean a otra que si lo aporta
    /// (P9, Ruling B-22, tanda 11). No llevan HeaderVista propio ni MargenVista: el contenedor (o
    /// el hijo, en el caso P9) ya los puso. Corren los invariantes que SI les aplican (opacidad,
    /// boton primario), mas uno propio e invertido
    /// (<see cref="VistaEmbebida_NoDuplicaElMargenDeVista"/>).
    /// </summary>
    public static readonly TheoryData<Type> VistasEmbebidas = new()
    {
        typeof(FuenteFinanciamientoListView),
        typeof(RubroGastoListView),
        typeof(LineaPoaListView),
        typeof(ZonaListView),
        typeof(DimensionTematicaListView),
        typeof(OrganismoResponsableListView),
        typeof(OrigenFinanciamientoListView),
        typeof(HistorialImportacionesView),
        typeof(NuevaImportacionView),
        typeof(AdjuntosPanelView),
        typeof(AdjuntosDocumentoPanelView),
        typeof(ClasificacionTareaPanelView),
        typeof(AccesoLimitadoView),
    };

    /// <summary>
    /// Vistas que no pueden entrar a ninguna lista del guardian, con su razon y con quien las
    /// custodia en su lugar (Task 13.4). Es una lista de EXCLUSION explicita: el test de
    /// cobertura de abajo exige que toda vista del ensamblado este en alguna lista o en esta.
    /// Son CINCO, no tres: la revision de exenciones de la Task 13.4 confirmo que solo hay DOS
    /// exenciones "vivas" mas arriba (VistasExentasPorEmbeberUnWizardP8 y
    /// VistasExentasPorPrimariosMutuamenteExcluyentesSinVm) -- el esbozo original de B3 preveia
    /// una tercera (VistasExentasPorMarcaDeAguaAprobada, LoginView) para el logo del municipio,
    /// pero el Ruling B-25 quedo SUPERSEDIDO: LoginView usa la variante Ruling B-11
    /// (Style Selector="Image.marca-agua", Priority=Style) en vez de Opacity local, asi que el
    /// guardian generico ya no la ve y no hizo falta ninguna exencion. Esta lista es un
    /// mecanismo DISTINTO: no exime un invariante, saca la vista de la cobertura por completo
    /// porque PatronHelpers.Montar no puede montarla (Window) o porque no es "una vista de
    /// contenido" (chrome de la app).
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
        typeof(RecalcularStockDialog),
        typeof(ReclasificarTareaDialog),
        // Window host de la app -- tambien queda fuera de Montar por la misma razon (Window).
        // Revisada en la Task 13.1: no tiene ningun literal visual que custodiar.
        typeof(MainWindow),
        // Cromo de la app (sidebar + ContentControl), no una vista de contenido navegable.
        // La custodian ShellMainViewGatesTests (15 casos) y SidebarContrasteTests.
        typeof(ShellMainView),
    };

    /// <summary>
    /// Vistas reescritas con el sistema v2 (spec 2026-09-28): los patrones P0-P9 de este guardián ya
    /// no les aplican (por ejemplo, P6 exige Border.card con padding 48 y la card v2 es
    /// TarjetaContenido con 32). Las custodian su comparación contra el mockup (VisualTests) y sus
    /// tests de UiTests/V2. La Fase 9 reemplaza este guardián entero (spec §8.2).
    /// </summary>
    public static readonly HashSet<Type> VistasMigradasAV2 = new()
    {
        typeof(LoginView),
        typeof(ResetAdminView),
        typeof(BloqueoLicenciaView),
        typeof(ActualizacionBannerView),
        typeof(ActualizacionModalView),
        typeof(ActualizacionBloqueoView),
        typeof(InicioView),
        typeof(ProductoListView),
        typeof(ProductoFormView),
        typeof(MovimientoFormControl),
        typeof(EntradaRegistroView),
        typeof(SalidaRegistroView),
        typeof(MovimientoHistorialView),
    };

    /// <summary>
    /// Con cuatro listas (InlineData, VistasDeLaTanda, VistasEmbebidas y
    /// VistasMigradasAV2) la trampa que el plan de B2 identifico con dos listas se multiplica: agregar una vista a una
    /// lista y olvidarla en otra no falla nada, simplemente deja invariantes sin correr. Este test
    /// enumera por reflexion todos los Control publicos, no abstractos, de los namespaces de
    /// vistas del ensamblado StockApp.Presentation y exige que cada uno este en alguna de las
    /// listas del guardian o en VistasFueraDelGuardian.
    ///
    /// El predicado de namespace excluye deliberadamente StockApp.Presentation.Controls (los 5
    /// TemplatedControl del design system: BadgeEstado, CampoFormulario, EstadoVacio, HeaderVista,
    /// TarjetaMetrica) -- no son vistas, son piezas del catalogo de patrones, y no tienen
    /// PatronHelpers.Montar ni sentido de "header/margen/opacidad de vista".
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

        var cubiertas = VistasDeLaTanda.Select(r => r.Data)
            .Concat(VistasEmbebidas.Select(r => r.Data))
            .Concat(VistasFueraDelGuardian)
            .Concat(VistasMigradasAV2)
            .ToHashSet();

        var huerfanas = todas.Where(t => !cubiertas.Contains(t)).ToList();
        Assert.True(huerfanas.Count == 0,
            "Vista(s) sin ninguna lista del guardian ni exclusion documentada: "
            + string.Join(", ", huerfanas.Select(t => t.Name)));
    }

    [AvaloniaTheory]
    [MemberData(nameof(VistasDeLaTanda))]
    public void Vista_TieneMargenExteriorEstandar(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var margen = PatronHelpers.MargenExteriorDe(vista);

        Assert.True(margen is not null,
            $"{tipoVista.Name} no tiene ningun panel con Margin distinto de default.");
        Assert.Equal(new Thickness(24), margen!.Value);
    }

    /// <summary>
    /// Ruling B-17: las vistas embebidas en un TabControl NO pueden traer MargenVista (24) propio
    /// -- el contenedor (MaestrosFinanzasView/ImportacionView) ya lo aplico, y duplicarlo da 48px
    /// de aire (bug C3 del esbozo de B2). Su Margin="0,12,0,0" (separacion visual con el borde de
    /// la pestaña) se conserva: el invariante es "distinto de Thickness(24)", no "sin margen".
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(VistasEmbebidas))]
    public void VistaEmbebida_NoDuplicaElMargenDeVista(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var margen = PatronHelpers.MargenExteriorDe(vista);

        Assert.True(margen != new Thickness(24),
            $"{tipoVista.Name} es embebida y trae MargenVista propio: el contenedor ya lo aplica, "
            + "queda 48px de aire duplicado.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(VistasDeLaTanda))]
    [MemberData(nameof(VistasEmbebidas))]
    public void Vista_NoTieneOpacidadesLiterales(Type tipoVista)
    {
        var vista = PatronHelpers.Montar(tipoVista);
        var literales = PatronHelpers.OpacidadesLiteralesDe(vista);

        Assert.True(literales.Count == 0,
            $"{tipoVista.Name} tiene {literales.Count} control(es) con Opacity literal: "
            + string.Join(", ", literales.Select(c => c.GetType().Name)));
    }

    /// <summary>
    /// Deuda de la Fase 1: una vista que se migra a v2 sale de VistasDeLaTanda y perdía este chequeo. El
    /// cromo del shell (sidebar v2) también: ShellMainView está fuera del guardián P0-P9, pero se puede montar.
    ///
    /// Cobertura PARCIAL, por diseño: PatronHelpers.Montar no asigna DataContext, así que lo que solo existe con un
    /// ViewModel (plantillas de celda de DataGrid, ItemTemplates, contenido tras bindings) no se realiza y no se
    /// mide. Para ShellMainView, que sin VM no dibuja el sidebar completo, lo cubre aparte
    /// ShellV2ConViewModel_NoTieneOpacidadesLiterales. Un literal dentro de una plantilla de otra vista migrada
    /// queda fuera de este chequeo.
    /// </summary>
    public static TheoryData<Type> VistasV2Montables =>
        new(VistasMigradasAV2.Append(typeof(ShellMainView)).OrderBy(t => t.Name));

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

    private static readonly Regex PiezaDelTemaViejo =
        new(@"SignoNegativoBrushConverter|ActivoOpacidadConverter|<c:BadgeEstado\b|<c:HeaderVista\b|<c:EstadoVacio\b", RegexOptions.Compiled);

    /// <summary>Ruta desde src del .axaml de una vista: el namespace StockApp.Presentation.X.Y es la carpeta X/Y.</summary>
    private static string RutaAxaml(Type vista) =>
        "StockApp.Presentation/" + vista.Namespace!["StockApp.Presentation.".Length..].Replace('.', '/') + "/" + vista.Name + ".axaml";

    public static TheoryData<Type> VistasV2ConAxaml => new(VistasMigradasAV2.OrderBy(t => t.Name));

    /// <summary>
    /// Una vista v2 no usa los conversores ni los controles del tema viejo (convención 11): se lee el TEXTO del
    /// .axaml, así cuenta también lo que vive en plantillas de celda, que el montaje sin ViewModel no realiza.
    /// </summary>
    [Theory]
    [MemberData(nameof(VistasV2ConAxaml))]
    public void VistaV2_NoUsaPiezasDelTemaViejo(Type vista) =>
        Assert.DoesNotMatch(PiezaDelTemaViejo, LeerDesdeSrc(RutaAxaml(vista)));

    [Fact]
    public void DetectorDePiezasViejas_VeElConversorYElControl()
    {
        Assert.Matches(PiezaDelTemaViejo, """<TextBlock Foreground="{Binding X, Converter={x:Static conv:SignoNegativoBrushConverter.Instance}}" />""");
        Assert.Matches(PiezaDelTemaViejo, """<c:BadgeEstado Texto="Stock negativo" />""");
        Assert.DoesNotMatch(PiezaDelTemaViejo, """<v2:Insignia Texto="Stock negativo" />""");
    }

    private static string LeerDesdeSrc(string rutaDesdeSrc, [CallerFilePath] string archivoDeEsteTest = "")
    {
        var ruta = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(archivoDeEsteTest)!, "..", "..", "src", rutaDesdeSrc));
        Assert.True(File.Exists(ruta), $"No existe {ruta}");
        return File.ReadAllText(ruta);
    }
    /// <summary>
    /// GAP DE PLAN encontrado al ejecutar la Task 8.1 (el plan esperaba "exactamente 2 fallos
    /// nuevos" en el Step 2 de esa task; hubo un tercero real). <see cref="ImportacionView"/>
    /// embebe <c>NuevaImportacionView</c> (P8, Ruling B-18 punto 3) como contenido de su primer
    /// TabItem, y <see cref="PatronHelpers.Montar"/> no asigna ViewModel: los tres
    /// <c>IsVisible="{Binding PasoActual, Converter=...}"</c> de <c>NuevaImportacionView</c>
    /// quedan sin resolver y caen al default `true` de la propiedad (mismo comportamiento que
    /// documenta la Task 6.7 para un <c>IsVisible</c> sin resolver). Eso deja 3 botones primarios
    /// "visibles" dentro del arbol de <c>ImportacionView</c> sin que <c>ImportacionView</c> tenga
    /// ninguna culpa: no agrega ningun <c>Classes="primary"</c> propio (verificado: los otros tres
    /// invariantes -- header, margen, opacidad -- SI le aplican y pasan en verde).
    ///
    /// Task 8.4: <c>NuevaImportacionView</c> se agrego a <see cref="VistasEmbebidas"/> (P8) y por
    /// lo tanto ENTRA a este mismo invariante generico -- y por la misma razon (montada sin VM,
    /// los 3 IsVisible sin resolver) tambien daria 3 primarios visibles. Se agrega ella misma
    /// aca, no solo su contenedor. El reemplazo real es
    /// <see cref="NuevaImportacionJerarquiaBotonesTests"/>: monta la vista CON un ViewModel real
    /// y verifica, para cada uno de los 3 valores de <c>PasoWizardImportacion</c>, que hay
    /// EXACTAMENTE un boton primario visible y que es el correcto -- eso custodia mas que este
    /// invariante generico, no menos.
    /// </summary>
    private static readonly HashSet<Type> VistasExentasPorEmbeberUnWizardP8 = new()
    {
        typeof(ImportacionView),
        typeof(NuevaImportacionView),
    };

    /// <summary>
    /// GAP DE PLAN encontrado en la Task 9.2 (Fase B, tanda 9): <c>DocumentoFormView</c> tiene dos
    /// <c>Classes="primary"</c> gateados por estado del ViewModel -- "Guardar" (alta,
    /// <c>IsVisible="{Binding EsNuevoDocumento}"</c>) y "Guardar cambios" (edicion,
    /// <c>IsVisible="{Binding PuedeEditar}"</c>). Verificado en <c>DocumentoFormViewModel.cs:88</c>:
    /// <c>PuedeEditar =&gt; !EsNuevoDocumento &amp;&amp; _documento is { EsActivo: true }</c> -- el
    /// termino <c>!EsNuevoDocumento</c> hace que los dos NUNCA sean true a la vez en produccion.
    /// Pero <see cref="PatronHelpers.Montar"/> no asigna DataContext (mismo mecanismo que el
    /// Ruling B-20 documenta para los dos <c>HeaderVista</c> de esta vista): sin VM real, ambos
    /// <c>IsVisible</c> caen al default `true` de la propiedad y el guardian ve dos primarios. Los
    /// 10 casos de <c>DocumentoFormViewGatesTests</c> (Task 9.0) ya ejercitan las dos ramas con un
    /// VM real via <c>ArbolVisual.EsVisibleEnArbol</c>; no se duplica esa cobertura aca.
    /// </summary>
    private static readonly HashSet<Type> VistasExentasPorPrimariosMutuamenteExcluyentesSinVm = new()
    {
        typeof(DocumentoFormView),
    };

    [AvaloniaTheory]
    [MemberData(nameof(VistasDeLaTanda))]
    [MemberData(nameof(VistasEmbebidas))]
    public void Vista_NoTieneUnSegundoBotonPrimario(Type tipoVista)
    {
        if (VistasExentasPorEmbeberUnWizardP8.Contains(tipoVista))
            return;
        if (VistasExentasPorPrimariosMutuamenteExcluyentesSinVm.Contains(tipoVista))
            return;

        var vista = PatronHelpers.Montar(tipoVista);
        var primarios = vista.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("primary") && ArbolVisual.EsVisibleEnArbol(b))
            .ToList();

        Assert.True(primarios.Count <= 1,
            $"{tipoVista.Name} tiene {primarios.Count} botones primarios visibles a la vez.");
    }
}
