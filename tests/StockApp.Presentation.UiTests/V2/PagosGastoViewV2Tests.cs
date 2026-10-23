using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Pagos del gasto v2 con la View REAL: breadcrumb con el origen, resumen, registro con errores por campo, grilla con
/// fila anulada y orden por click, panel de adjuntos (en solo lectura con un pago anulado), solo lectura por permiso y por gasto
/// anulado, estados de carga, y revocación en caliente por el camino del shell.</summary>
public class PagosGastoViewV2Tests
{
    private static readonly Proveedor ElectroSur = new() { Id = 3, Nombre = "Electro Sur S.R.L.", Activo = true };

    private static readonly FuenteFinanciamiento Rentas = new() { Id = 1, Nombre = "Rentas Generales", Caja = Caja.Local, Activo = true };
    private static readonly FuenteFinanciamiento Tributos = new() { Id = 2, Nombre = "Tributos", Caja = Caja.Local, Activo = true };
    private static readonly FuenteFinanciamiento Figm = new() { Id = 3, Nombre = "FIGM", Caja = Caja.Poa, Activo = true };

    private static Gasto UnGasto(bool activo = true, bool conPagos = true)
    {
        var gasto = new Gasto
        {
            Id = 6, ProveedorId = 3, Proveedor = ElectroSur, NumeroFactura = "A-0001190", Detalle = "Luminarias LED plaza Artigas",
            Fecha = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), MontoTotal = 310_000m, FuenteFinanciamientoId = 1,
            RubroGastoId = 3, CondicionPago = CondicionPago.Credito,
            FechaVencimiento = new DateTime(2030, 11, 30, 0, 0, 0, DateTimeKind.Utc), Activo = activo,
        };
        if (conPagos)
        {
            gasto.Pagos.Add(new PagoGasto { Id = 61, GastoId = 6, Fecha = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), Monto = 100_000m, Nota = "Recibo 2231" });
            gasto.Pagos.Add(new PagoGasto { Id = 62, GastoId = 6, Fecha = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc), Monto = 50_000m, Nota = "Cargado dos veces", Activo = false });
            gasto.Pagos.Add(new PagoGasto { Id = 63, GastoId = 6, Fecha = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), Monto = 50_000m, Nota = "Recibo 2240" });
        }
        return gasto;
    }

    private static AdjuntosPorPagoFake ConAdjuntos()
    {
        var adjuntos = new AdjuntosPorPagoFake();
        adjuntos.PorPago[61] = [new AdjuntoDto(1, "recibo-2231.pdf", "application/pdf", 96_300, null, 61, new DateTime(2026, 10, 12, 15, 20, 0, DateTimeKind.Utc))];
        adjuntos.PorPago[62] = [new AdjuntoDto(2, "comprobante-duplicado.pdf", "application/pdf", 61_440, null, 62, new DateTime(2026, 10, 13, 11, 5, 0, DateTimeKind.Utc))];
        return adjuntos;
    }

    private static SesionFake Admin() => new(RolUsuario.Admin);
    private static SesionFake ConPermiso() => new(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarPagos);
    private static SesionFake SoloVer() => new(RolUsuario.Operador, Permisos.VerFinanzas);

    private sealed record Montaje(Window Ventana, PagosGastoView Vista, PagosGastoViewModel Vm, PagosFake Servicio,
        AdjuntosPorPagoFake Adjuntos, NavigationRecorderFake Navegacion);

    private static PagosGastoViewModel NuevoVm(PagosFake servicio, ICurrentSession sesion, INavigationService navegacion, AdjuntosPorPagoFake adjuntos,
        FuentesActivasFake? fuentes = null)
    {
        var confirmacion = new ConfirmacionServiceFake();
        var panel = new AdjuntosPanelViewModel(adjuntos, new ServicioSeleccionArchivoFake(), new ServicioAperturaArchivoFake(), confirmacion, sesion);
        var vm = new PagosGastoViewModel(servicio, fuentes ?? new FuentesActivasFake { Activas = [Rentas, Tributos, Figm] }, sesion, navegacion, confirmacion, panel);
        vm.CargarParaGasto(new Gasto { Id = 6 });
        return vm;
    }

    private static Montaje Montar(Gasto? gasto = null, ICurrentSession? sesion = null, double ancho = 1040, double alto = 760,
        Action<PagosGastoViewModel>? preparar = null, PagosFake? servicio = null, AdjuntosPorPagoFake? adjuntos = null,
        FuentesActivasFake? fuentes = null)
    {
        servicio ??= new PagosFake { Gasto = gasto ?? UnGasto() };
        adjuntos ??= ConAdjuntos();
        var navegacion = new NavigationRecorderFake();
        var vm = NuevoVm(servicio, sesion ?? Admin(), navegacion, adjuntos, fuentes);
        preparar?.Invoke(vm);
        var vista = new PagosGastoView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        ventana.DataTemplates.Add(new ViewLocator()); // el ContentControl del panel de adjuntos resuelve su View por el ViewLocator de la app
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, adjuntos, navegacion);
    }

    private static T Parte<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);
    private static Rect EnVentana(Window ventana, Control c) => new(c.TranslatePoint(new Point(0, 0), ventana)!.Value, c.Bounds.Size);
    private static IEnumerable<string> Errores(Control c) => DataValidationErrors.GetErrors(c)?.Select(e => e?.ToString() ?? "") ?? [];

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGridRow Fila(Montaje m, int pagoId) =>
        Parte<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridRow>().Single(r => r.DataContext is PagoGasto p && p.Id == pagoId);

    private static Button? BotonAnularDe(Montaje m, int pagoId) =>
        Fila(m, pagoId).GetVisualDescendants().OfType<Button>().SingleOrDefault();

    private static Button? BotonDelPanel(Montaje m, string texto) =>
        m.Vista.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => (b.Content as string) == texto && b.Name != "BotonRegistrar");

    private static void Seleccionar(Montaje m, int pagoId)
    {
        m.Vm.PagoSeleccionado = m.Vm.Pagos.Single(p => p.Id == pagoId);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    // ── Estructura y encabezado ─────────────────────────────────────────────────

    [AvaloniaFact]
    public void Raiz_TieneUi2_BreadcrumbConElOrigen_RegistrarPagoEsElUnicoPrimario()
    {
        var m = Montar();
        var encabezado = Parte<EncabezadoPagina>(m, "Encabezado");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Gastos y facturas", encabezado.BreadcrumbOrigen);
        Assert.Equal("Pagos de la factura", encabezado.BreadcrumbActual);
        Assert.Equal("Pagos de la factura", encabezado.Titulo);
        Assert.Equal(m.Vm.TituloGasto, encabezado.Subtitulo);
        Assert.Equal("Luminarias LED plaza Artigas — factura A-0001190 (Electro Sur S.R.L.)", encabezado.Subtitulo);
        Assert.Same(m.Vm.VolverCommand, encabezado.BreadcrumbComando);
        var primarios = m.Vista.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario") && Visible(b)).ToList();
        Assert.Equal("BotonRegistrar", Assert.Single(primarios).Name);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Volver");
    }

    [AvaloniaFact]
    public void DesdeElCalendario_ElBreadcrumbDiceCalendarioDePagos()
    {
        var m = Montar(preparar: vm => vm.ConfigurarVolver(() => { }, "Calendario de pagos"));

        Assert.Equal("Calendario de pagos", Parte<EncabezadoPagina>(m, "Encabezado").BreadcrumbOrigen);
    }

    [AvaloniaFact]
    public void Breadcrumb_ClickReal_VuelveAlOrigen_AunSinPermisoDeRegistrar()
    {
        var m = Montar(sesion: SoloVer());
        var origen = Parte<EncabezadoPagina>(m, "Encabezado").GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_BreadcrumbOrigen");

        Clickear(m.Ventana, origen);

        Assert.Equal(typeof(GastosViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Resumen_MuestraLosImportesYLaInsigniaDeEstadoConSuTono()
    {
        var m = Montar();

        Assert.Equal("$ 310.000,00", Parte<TextBlock>(m, "ValorMontoTotal").Text);
        Assert.Equal("$ 150.000,00", Parte<TextBlock>(m, "ValorPagado").Text);
        Assert.Equal("$ 160.000,00", Parte<TextBlock>(m, "ValorSaldo").Text);
        var estado = Parte<Insignia>(m, "InsigniaEstado");
        Assert.Equal("Parcial", estado.Texto);
        Assert.Equal(VarianteInsignia.Advertencia, estado.Variante);
    }

    [AvaloniaFact]
    public void Resumen_GastoAnulado_SaldoConGuion()
    {
        var m = Montar(UnGasto(activo: false));

        Assert.Equal("—", Parte<TextBlock>(m, "ValorSaldo").Text);
        Assert.Equal("Anulada", Parte<Insignia>(m, "InsigniaEstado").Texto);
        Assert.Equal(VarianteInsignia.Neutro, Parte<Insignia>(m, "InsigniaEstado").Variante);
    }

    [AvaloniaTheory]
    [InlineData(1040.0)]
    [InlineData(1126.0)]
    public void Cards_ATodoElAnchoDeLaPagina_ApiladasCon16(double ancho)
    {
        var m = Montar(ancho: ancho);
        Rect R(string n) => EnVentana(m.Ventana, Parte<TarjetaContenido>(m, n));

        Assert.Equal(ancho - 56, R("TarjetaResumen").Width, 1);
        Assert.Equal(28, R("TarjetaResumen").X);
        Assert.Equal(R("TarjetaResumen").Bottom + 16, R("TarjetaRegistrar").Y, 1);
        Assert.Equal(R("TarjetaRegistrar").Bottom + 16, R("TarjetaPagos").Y, 1);
        Assert.Equal(R("TarjetaPagos").Bottom + 16, R("TarjetaAdjuntos").Y, 1);
    }

    [AvaloniaTheory]
    [InlineData(1040.0)]
    [InlineData(1126.0)]
    public void FilaRegistrar_FechaMontoFuenteNotaYBoton_ConSusAnchos_ElBotonAlineadoConLosInputs(double ancho)
    {
        var m = Montar(ancho: ancho);
        Rect R(string n) => EnVentana(m.Ventana, Parte<Control>(m, n));

        Assert.Equal(140, R("SelectorFecha").Width, 1);
        Assert.Equal(160, R("CajaMonto").Width, 1);
        Assert.Equal(R("SelectorFecha").Right + 8, R("CajaMonto").X, 1);
        Assert.Equal(R("CajaMonto").Right + 8, R("ComboFuente").X, 1);
        Assert.Equal(220, R("ComboFuente").Width, 1);
        Assert.Equal(R("ComboFuente").Right + 8, R("CajaNota").X, 1);
        Assert.Equal(R("CajaNota").Right + 8, R("BotonRegistrar").X, 1);
        Assert.Equal(R("CajaMonto").Y, R("BotonRegistrar").Y, 1);   // alineado arriba con los inputs
        Assert.Equal(36, R("BotonRegistrar").Height, 1);
    }

    [AvaloniaFact]
    public void ConUnErrorDeCampo_ElBotonSigueAlineadoConLosInputs_NoBajaConElMensaje()
    {
        var m = Montar();
        var inputsY = EnVentana(m.Ventana, Parte<TextBox>(m, "CajaMonto")).Y;

        Parte<TextBox>(m, "CajaMonto").Text = "1.5";
        Dispatcher.UIThread.RunJobs();

        Assert.NotEmpty(Errores(Parte<TextBox>(m, "CajaMonto")));
        Assert.Equal(inputsY, EnVentana(m.Ventana, Parte<TextBox>(m, "CajaMonto")).Y, 1);
        Assert.Equal(inputsY, EnVentana(m.Ventana, Parte<Button>(m, "BotonRegistrar")).Y, 1);
    }

    // ── Salir de un campo lo marca como tocado (TocarAlSalir del code-behind) ──────────────────

    [AvaloniaFact]
    public void SalirDeMontoSinEscribir_MuestraElObligatorio()
    {
        var m = Montar();
        Parte<TextBox>(m, "CajaMonto").Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Errores(Parte<TextBox>(m, "CajaMonto")));

        Parte<TextBox>(m, "CajaNota").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { PagosGastoViewModel.MensajeMontoObligatorio }, Errores(Parte<TextBox>(m, "CajaMonto")));
    }

    [AvaloniaFact]
    public void FechaVaciada_MuestraSuErrorDebajoDelSelector_AlSalirDeEl()
    {
        var m = Montar();
        var selector = Parte<CalendarDatePicker>(m, "SelectorFecha");
        selector.Focus();
        Dispatcher.UIThread.RunJobs();

        selector.SelectedDate = null;
        Parte<TextBox>(m, "CajaNota").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { PagosGastoViewModel.MensajeFechaObligatoria }, Errores(selector));
        var mensaje = m.Vista.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == PagosGastoViewModel.MensajeFechaObligatoria);
        Assert.True(Visible(mensaje));
        // El DataValidationErrors vive dentro del control: sus Bounds incluyen el mensaje, así que "debajo" se mide contra la caja de 36.
        var caja = EnVentana(m.Ventana, selector);
        Assert.True(EnVentana(m.Ventana, mensaje).Y >= caja.Y + 36 - 1,
            $"el error de Fecha tiene que verse DEBAJO de la caja del selector: mensaje {EnVentana(m.Ventana, mensaje)} selector {caja}");
        Assert.False(Parte<Button>(m, "BotonRegistrar").IsEffectivelyEnabled);
    }

    // ── Grilla ──────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Grilla_FechaDelPago_SinCorrimientoDeDia()
    {
        var m = Montar();

        var textos = Fila(m, 61).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("12/10/2026", textos);   // el texto PINTADO: medianoche UTC no puede verse 11/10
        Assert.Contains("$ 100.000,00", textos);
        Assert.Contains("Recibo 2231", textos);
    }

    [AvaloniaFact]
    public void Grilla_PagoAnulado_TextoTerciarioInsigniaYSinAnular()
    {
        var m = Montar();
        var terciario = MontajeV2.ColorDe((IBrush?)Avalonia.Application.Current!.FindResource("V2TextoTerciarioBrush"));

        var textos = Fila(m, 62).GetVisualDescendants().OfType<TextBlock>().Where(t => t.Parent is not null && !string.IsNullOrEmpty(t.Text)
            && t.FindAncestorOfType<Insignia>() is null).ToList();
        Assert.NotEmpty(textos);
        Assert.All(textos, t =>
        {
            Assert.Contains("inactivo", t.Classes);
            Assert.Equal(terciario, MontajeV2.ColorDe(t.Foreground));
        });
        var insignia = Fila(m, 62).GetVisualDescendants().OfType<Insignia>().Single();
        Assert.True(Visible(insignia));
        Assert.Equal("Anulado", insignia.Texto);
        Assert.False(Visible(BotonAnularDe(m, 62)!));
        Assert.True(Visible(BotonAnularDe(m, 61)!));
        Assert.False(Visible(Fila(m, 61).GetVisualDescendants().OfType<Insignia>().Single()));
    }

    [AvaloniaFact]
    public void AccionAnular_EntraEnLaFilaSinRecortar()
    {
        var m = Montar();
        var fila = EnVentana(m.Ventana, Fila(m, 61));
        var boton = BotonAnularDe(m, 61)!;
        var caja = EnVentana(m.Ventana, boton);

        Assert.Equal(36, caja.Height, 1);
        Assert.True(caja.Top >= fila.Top - 0.5 && caja.Bottom <= fila.Bottom + 0.5, $"el botón {caja} se sale de la fila {fila}");
        var texto = boton.GetVisualDescendants().OfType<ContentPresenter>().First();
        Assert.True(texto.Bounds.Height >= 15, "el texto del botón quedó aplastado por el padding de la celda");
    }

    [AvaloniaFact]
    public void Grilla_ClickEnElHeaderMonto_Ordena_YOtroClickInvierte()
    {
        var m = Montar();
        var grilla = Parte<DataGrid>(m, "Grilla");
        void Clic() => Clickear(m.Ventana, grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => Equals(h.Content, "MONTO")));

        Clic();
        Assert.Equal(new[] { 50_000m, 50_000m, 100_000m }, m.Vm.PagosView.Cast<PagoGasto>().Select(p => p.Monto));
        Clic();
        Assert.Equal(100_000m, m.Vm.PagosView.Cast<PagoGasto>().First().Monto);
    }

    [AvaloniaFact]
    public void SeleccionarFilaEnLaGrilla_ActualizaElPagoSeleccionadoDelVm()
    {
        var m = Montar();

        Parte<DataGrid>(m, "Grilla").SelectedItem = m.Vm.Pagos.Single(p => p.Id == 63);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(63, m.Vm.PagoSeleccionado!.Id);
    }

    [AvaloniaFact]
    public void SinPagos_EstadoVacioEnLaCardDePagos_SinGrilla()
    {
        var m = Montar(UnGasto(conPagos: false));

        var vacio = Parte<EstadoVacio>(m, "EstadoSinPagos");
        Assert.True(Visible(vacio));
        Assert.Equal("Todavía no hay pagos", vacio.Titulo);
        Assert.Equal("Los pagos que se registren van a aparecer acá.", vacio.Mensaje);
        Assert.False(Visible(Parte<DataGrid>(m, "Grilla")));
        Assert.Equal("Pagos · 0", Parte<TextBlock>(m, "TituloPagos").Text);
    }

    [AvaloniaFact]
    public void TituloPagos_LlevaElConteo()
    {
        Assert.Equal("Pagos · 3", Parte<TextBlock>(Montar(), "TituloPagos").Text);
    }

    // ── Adjuntos (Q-2 B) ────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void SinSeleccion_LaCardDeAdjuntosPideElegirUnPago()
    {
        var m = Montar();

        var texto = Parte<TextBlock>(m, "TextoElegiPago");
        Assert.True(Visible(texto));
        Assert.Equal("Elegí un pago de la lista para ver sus adjuntos.", texto.Text);
        Assert.False(Visible(Parte<ContentControl>(m, "PanelAdjuntos")));
    }

    [AvaloniaFact]
    public void ConUnPagoActivoSeleccionado_MuestraElPanelConSuAdjuntoYSusBotones()
    {
        var m = Montar();

        Seleccionar(m, 61);

        Assert.False(Visible(Parte<TextBlock>(m, "TextoElegiPago")));
        Assert.True(Visible(Parte<ContentControl>(m, "PanelAdjuntos")));
        var lista = m.Vista.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("recibo-2231.pdf", lista);
        Assert.True(Visible(BotonDelPanel(m, "Agregar")!));
        Assert.True(Visible(BotonDelPanel(m, "Quitar")!));
        Assert.True(Visible(BotonDelPanel(m, "Ver")!));
        Assert.False(Visible(m.Vista.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TextoAvisoSoloLectura")));
    }

    [AvaloniaFact]
    public void ConUnPagoAnuladoSeleccionado_MuestraElPanelEnSoloLecturaConSuAviso()
    {
        var m = Montar();

        Seleccionar(m, 62);

        Assert.True(Visible(Parte<ContentControl>(m, "PanelAdjuntos")));
        Assert.Contains("comprobante-duplicado.pdf", m.Vista.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.True(Visible(BotonDelPanel(m, "Ver")!));
        Assert.False(Visible(BotonDelPanel(m, "Agregar")!));
        Assert.False(Visible(BotonDelPanel(m, "Quitar")!));
        var aviso = m.Vista.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TextoAvisoSoloLectura");
        Assert.True(Visible(aviso));
        Assert.Equal("Pago anulado: los adjuntos son solo de consulta", aviso.Text);
    }

    [AvaloniaFact]
    public void PasarDeUnPagoAnuladoAUnActivo_VuelveAgregarYQuitarSinElAviso()
    {
        var m = Montar();
        Seleccionar(m, 62);

        Seleccionar(m, 61);

        Assert.True(Visible(BotonDelPanel(m, "Agregar")!));
        Assert.False(Visible(m.Vista.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TextoAvisoSoloLectura")));
    }

    [AvaloniaFact]
    public void SeleccionarUnPago_TraeElPanelDeAdjuntosALaVista_YAlAbrirNoScrollea()
    {
        var m = Montar(alto: 460);
        var desplazamiento = Parte<ScrollViewer>(m, "Desplazamiento");
        var tarjetaAntes = EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "TarjetaAdjuntos"));
        Assert.Equal(0, desplazamiento.Offset.Y);   // al abrir la pantalla no se scrollea sola
        Assert.True(tarjetaAntes.Bottom > m.Ventana.Height, $"Precondición: sin selección la card no entra en 460: {tarjetaAntes}");

        Seleccionar(m, 61);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var panel = EnVentana(m.Ventana, Parte<ContentControl>(m, "PanelAdjuntos"));
        var viewport = EnVentana(m.Ventana, desplazamiento);
        Assert.True(panel.Bottom <= viewport.Bottom + 1, $"el panel de adjuntos {panel} quedó fuera de la vista {viewport}");
    }

    // ── Permisos y estado del gasto ─────────────────────────────────────────────

    [AvaloniaFact]
    public void OperadorSinRegistrarPagos_SoloLectura()
    {
        var m = Montar(sesion: SoloVer());

        Assert.False(Visible(Parte<TarjetaContenido>(m, "TarjetaRegistrar")));
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<ComboBox>(), c => c.Name == "ComboFuente" && Visible(c));
        Assert.True(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        Assert.False(Visible(Parte<Insignia>(m, "InsigniaAnulado")));
        Assert.False(Visible(BotonAnularDe(m, 61)!));
        Seleccionar(m, 61);
        Assert.False(Visible(BotonDelPanel(m, "Agregar")!));
        Assert.False(Visible(BotonDelPanel(m, "Quitar")!));
        Assert.True(Visible(BotonDelPanel(m, "Ver")!));
    }

    [AvaloniaFact]
    public void OperadorConRegistrarPagos_RegistraYAnula()
    {
        var m = Montar(sesion: ConPermiso());

        Assert.True(Visible(Parte<TarjetaContenido>(m, "TarjetaRegistrar")));
        Assert.True(Visible(Parte<ComboBox>(m, "ComboFuente")));
        Assert.False(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        Assert.True(Visible(BotonAnularDe(m, 61)!));
        Assert.False(Visible(BotonAnularDe(m, 62)!));
    }

    [AvaloniaFact]
    public void GastoAnulado_InsigniasAlertaYSinRegistrar()
    {
        var m = Montar(UnGasto(activo: false));

        Assert.True(Visible(Parte<Insignia>(m, "InsigniaAnulado")));
        Assert.True(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        var alerta = Parte<Alerta>(m, "AlertaAnulado");
        Assert.True(Visible(alerta));
        Assert.Equal("Este gasto está anulado: se pueden consultar sus pagos, pero no registrar ni anular.", alerta.Texto);
        Assert.False(Visible(Parte<TarjetaContenido>(m, "TarjetaRegistrar")));
        Assert.False(Visible(BotonAnularDe(m, 61)!));
    }

    [AvaloniaFact]
    public void GastoActivo_SinInsigniasNiAlerta()
    {
        var m = Montar();

        Assert.False(Visible(Parte<Insignia>(m, "InsigniaAnulado")));
        Assert.False(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        Assert.False(Visible(Parte<Alerta>(m, "AlertaAnulado")));
        Assert.False(Visible(Parte<Alerta>(m, "AlertaError")));
        Assert.False(Visible(Parte<EstadoVacio>(m, "EstadoSinPermiso")));
        // Hijos de una card oculta: no están en el árbol visual, se buscan por nombre.
        Assert.False(Visible(m.Vista.FindControl<EstadoVacio>("EstadoSinConexion")!));
        Assert.False(Visible(m.Vista.FindControl<EstadoCargando>("IndicadorCarga")!));
        Assert.True(Visible(Parte<StackPanel>(m, "Contenido")));
    }

    // ── Registrar: errores por campo (Q-4 B) ────────────────────────────────────

    [AvaloniaFact]
    public void MontoIlegible_ErrorDeCampoBajoElMonto_YRegistrarBloqueado()
    {
        var m = Montar();

        Parte<TextBox>(m, "CajaMonto").Text = "1.5";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "Usá coma para los decimales: 1,5" }, Errores(Parte<TextBox>(m, "CajaMonto")));
        Assert.False(Parte<Button>(m, "BotonRegistrar").IsEffectivelyEnabled);
        Assert.False(Visible(Parte<Alerta>(m, "AlertaError")));
    }

    [AvaloniaFact]
    public void MontoVacio_SinTocar_NoApareceEnRojo_PeroRegistrarEstaDeshabilitado()
    {
        var m = Montar();

        Assert.Empty(Errores(Parte<TextBox>(m, "CajaMonto")));
        Assert.False(Parte<Button>(m, "BotonRegistrar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void MontoValidoYFuenteElegida_HabilitaRegistrar()
    {
        var m = Montar();

        m.Vm.MontoTexto = "100";
        Parte<ComboBox>(m, "ComboFuente").SelectedItem = Rentas;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Parte<Button>(m, "BotonRegistrar").IsEffectivelyEnabled);
        Assert.Empty(Errores(Parte<TextBox>(m, "CajaMonto")));
    }

    [AvaloniaFact]
    public async Task RegistrarPago_ClickReal_RegistraYDejaElFormularioLimpioSinRojo()
    {
        var m = Montar();
        m.Vm.MontoTexto = "100";
        m.Vm.Nota = "saldo";
        Parte<ComboBox>(m, "ComboFuente").SelectedItem = Tributos;
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, Parte<Button>(m, "BotonRegistrar"));
        await (m.Vm.RegistrarPagoCommand.ExecutionTask ?? Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();

        var pago = Assert.Single(m.Servicio.Registrados);
        Assert.Equal((100m, "saldo", 2, Caja.Local), (pago.Monto, pago.Nota, pago.FuenteFinanciamientoId, pago.Caja));
        Assert.Same(Tributos, Parte<ComboBox>(m, "ComboFuente").SelectedItem);   // tras registrar la fuente se conserva
        Assert.Equal(string.Empty, Parte<TextBox>(m, "CajaMonto").Text ?? string.Empty);
        Assert.Empty(Errores(Parte<TextBox>(m, "CajaMonto")));
    }

    [AvaloniaFact]
    public async Task ErrorDelServidor_AlertaDebajoDeLaFila()
    {
        var m = Montar();
        m.Servicio.FallaAlRegistrar = new ReglaDeNegocioException("El pago supera el saldo pendiente.");
        m.Vm.MontoTexto = "999999";
        m.Vm.FuenteSeleccionada = Rentas;
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, Parte<Button>(m, "BotonRegistrar"));
        await (m.Vm.RegistrarPagoCommand.ExecutionTask ?? Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();

        var alerta = Parte<Alerta>(m, "AlertaError");
        Assert.True(Visible(alerta));
        Assert.Equal("El pago supera el saldo pendiente.", alerta.Texto);
        Assert.True(EnVentana(m.Ventana, alerta).Y >= EnVentana(m.Ventana, Parte<TextBox>(m, "CajaMonto")).Bottom);
    }

    [AvaloniaFact]
    public async Task AnularPago_ClickReal_ConfirmaYAnula()
    {
        var m = Montar();

        Clickear(m.Ventana, BotonAnularDe(m, 61)!);
        await (m.Vm.AnularPagoCommand.ExecutionTask ?? Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal((6, 61), Assert.Single(m.Servicio.AnuladosPagos));
    }

    // ── Caja POA (2026-10-21): literal Local obligatorio, sin selector de caja ───────────────────────────

    [AvaloniaFact]
    public void ComboFuente_SoloLiteralesLocales_SinPreseleccion()
    {
        var m = Montar();
        var combo = Parte<ComboBox>(m, "ComboFuente");

        Assert.Equal(new[] { Rentas, Tributos }, combo.Items.Cast<FuenteFinanciamiento>().ToArray());
        Assert.Null(combo.SelectedItem);
        Assert.False(Visible(Parte<Alerta>(m, "AlertaSinLiterales")));
    }

    [AvaloniaFact]
    public void SinComboDeCaja_ElPagoManualSiempreEsLocal()
    {
        var m = Montar();

        Assert.Throws<InvalidOperationException>(() => Parte<ComboBox>(m, "ComboCaja"));
    }

    [AvaloniaFact]
    public void SinFuente_RegistrarDeshabilitado_ConTodoLoDemasValido_YSeHabilitaAlElegirla()
    {
        var m = Montar();
        m.Vm.MontoTexto = "100";   // fecha de hoy, monto legible: falta SOLO la fuente
        Dispatcher.UIThread.RunJobs();

        Assert.False(Parte<Button>(m, "BotonRegistrar").IsEffectivelyEnabled);

        Parte<ComboBox>(m, "ComboFuente").SelectedItem = Rentas;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Parte<Button>(m, "BotonRegistrar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void SalirDeLaFuenteSinElegir_MensajeEnElCombo()
    {
        var m = Montar();
        var combo = Parte<ComboBox>(m, "ComboFuente");
        combo.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(combo.IsKeyboardFocusWithin, "Precondición: el combo tiene el foco.");

        Parte<TextBox>(m, "CajaMonto").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { PagosGastoViewModel.MensajeFuenteObligatoria }, Errores(combo));
    }

    [AvaloniaFact]
    public void SinLiteralesLocales_AlertaVisibleConElTexto()
    {
        var m = Montar(fuentes: new FuentesActivasFake { Activas = [Figm] });

        var alerta = Parte<Alerta>(m, "AlertaSinLiterales");
        Assert.True(Visible(alerta));
        Assert.Equal("No hay literales asignados a la caja Local; configuralos en Fuentes de financiamiento", alerta.Texto);
        Assert.Equal(VarianteAlerta.Advertencia, alerta.Variante);
        Assert.Empty(Parte<ComboBox>(m, "ComboFuente").Items);
    }

    [AvaloniaFact]
    public void Grilla_MuestraFuenteYCajaDeCadaPago()
    {
        var gasto = UnGasto(conPagos: false);
        var automatico = PagoGasto.Automatico(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), 100_000m, "Pago contado (automático)", Caja.Poa, Figm.Id);
        automatico.Id = 61; automatico.GastoId = 6; automatico.FuenteFinanciamiento = Figm;
        gasto.Pagos.Add(automatico);
        gasto.Pagos.Add(new PagoGasto
        {
            Id = 62, GastoId = 6, Fecha = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), Monto = 50_000m, Nota = "Recibo 2240",
            Caja = Caja.Local, FuenteFinanciamientoId = Rentas.Id, FuenteFinanciamiento = Rentas,
        });
        var m = Montar(gasto);
        var grilla = Parte<DataGrid>(m, "Grilla");

        Assert.Equal(new object?[] { "FECHA", "MONTO", "CAJA", "FUENTE", "NOTA", "ESTADO", "" }, grilla.Columns.Select(c => c.Header).ToArray());
        Assert.Equal("POA", TextoDe(m, 61, "CAJA"));
        Assert.Equal("FIGM", TextoDe(m, 61, "FUENTE"));
        Assert.Equal("Local", TextoDe(m, 62, "CAJA"));
        Assert.Equal("Rentas Generales", TextoDe(m, 62, "FUENTE"));
    }

    private static string? TextoDe(Montaje m, int pagoId, string encabezado)
    {
        var grilla = Parte<DataGrid>(m, "Grilla");
        var indice = grilla.Columns.ToList().FindIndex(c => (string?)c.Header == encabezado);
        var celda = Fila(m, pagoId).GetVisualDescendants().OfType<DataGridCell>().ElementAt(indice);
        return celda.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
    }

    // ── Estados de pantalla ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void CargaPendiente_IndicadorDeCargaYSinCards()
    {
        var m = Montar(servicio: new PagosFake { Pendiente = new TaskCompletionSource<Gasto>() });

        var indicador = Parte<EstadoCargando>(m, "IndicadorCarga");
        Assert.True(Visible(indicador));
        Assert.Equal("Cargando los pagos…", indicador.Texto);
        Assert.False(Visible(Parte<StackPanel>(m, "Contenido")));
    }

    [AvaloniaFact]
    public async Task SinServidor_EstadoSinConexionConReintentar_EnLugarDeLasCards()
    {
        var servicio = new PagosFake { Falla = new ServidorNoDisponibleException() };
        var m = Montar(servicio: servicio);
        Assert.True(Visible(Parte<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.False(Visible(Parte<StackPanel>(m, "Contenido")));

        servicio.Falla = null;
        servicio.Gasto = UnGasto();
        Clickear(m.Ventana, Parte<Button>(m, "BotonReintentar"));
        await (m.Vm.ReintentarCargaCommand.ExecutionTask ?? Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Parte<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Parte<StackPanel>(m, "Contenido")));
        Assert.Equal(3, m.Vm.Pagos.Count);
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDelContenido()
    {
        var m = Montar(servicio: new PagosFake { Falla = new UnauthorizedAccessException("403") });

        var estado = Parte<EstadoVacio>(m, "EstadoSinPermiso");
        Assert.True(Visible(estado));
        Assert.Equal(PagosGastoViewModel.MensajeSinPermisoPagos, estado.Mensaje);
        Assert.False(Visible(Parte<StackPanel>(m, "Contenido")));
    }

    // ── Revocación en caliente dentro del shell ─────────────────────────────────

    private sealed record MontajeShell(Window Ventana, PagosGastoView Vista, PagosGastoViewModel Vm, ShellMainViewModel Shell,
        SesionFake Sesion, HashSet<string> Tras);

    private static MontajeShell MontarEnShell()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarPagos);
        var tras = new HashSet<string> { Permisos.VerFinanzas };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = NuevoVm(new PagosFake { Gasto = UnGasto() }, sesion, new NavigationRecorderFake(), ConAdjuntos());
        shell.CurrentContent = vm;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new MontajeShell(ventana, ventana.GetVisualDescendants().OfType<PagosGastoView>().Single(), vm, shell, sesion, tras);
    }

    private static async Task Revocar(MontajeShell s)
    {
        await ManejadorAccesoRevocado.ManejarAsync(s.Sesion, new AuthServiceFake(s.Sesion, s.Tras), new ConfirmacionServiceFake(),
            s.Shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task RevocacionDeRegistrarPagos_DentroDelShell_PasaASoloLecturaSinBorrarLoTipeado()
    {
        var s = MontarEnShell();
        s.Vm.Nota = "recibo 9";
        Dispatcher.UIThread.RunJobs();
        Assert.True(Visible(MontajeV2.Parte<TarjetaContenido>(s.Vista, "TarjetaRegistrar")), "Precondición: con finanzas.pagos se registra.");
        Assert.True(Visible(s.Vista.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext is PagoGasto { Id: 61 }).GetVisualDescendants().OfType<Button>().Single()));

        await Revocar(s);

        Assert.False(Visible(MontajeV2.Parte<TarjetaContenido>(s.Vista, "TarjetaRegistrar")));
        Assert.True(Visible(MontajeV2.Parte<Insignia>(s.Vista, "InsigniaSoloLectura")));
        Assert.All(s.Vista.GetVisualDescendants().OfType<DataGridRow>().SelectMany(r => r.GetVisualDescendants().OfType<Button>()), b => Assert.False(Visible(b)));
        Assert.Equal("recibo 9", s.Vm.Nota);
        s.Vm.MontoTexto = "10";
        Assert.False(s.Vm.RegistrarPagoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Un403DelPanel_DentroDelShell_OcultaAgregarYQuitar()
    {
        var s = MontarEnShell();
        s.Vm.PagoSeleccionado = s.Vm.Pagos.Single(p => p.Id == 61);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Button? Boton(string t) => s.Vista.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => (b.Content as string) == t && b.Name != "BotonRegistrar");
        Assert.True(Visible(Boton("Agregar")!), "Precondición: con finanzas.pagos el panel se modifica.");
        Assert.True(Visible(Boton("Quitar")!));

        await Revocar(s);

        Assert.False(Visible(Boton("Agregar")!));
        Assert.False(Visible(Boton("Quitar")!));
        Assert.True(Visible(Boton("Ver")!));
    }
}
