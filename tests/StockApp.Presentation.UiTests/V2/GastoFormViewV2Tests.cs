using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
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

/// <summary>Formulario de gasto v2 con la View REAL: breadcrumb en los tres modos, card de 760 con cuatro secciones,
/// errores por campo, pie fijo fuera del scroll, tilde a crédito fijo en edición, solo lectura (permiso y gasto anulado),
/// adjuntos, carga inicial sin servidor y con 403, y revocación en caliente por el camino del shell.</summary>
public class GastoFormViewV2Tests
{
    private static readonly Proveedor Barraca = new() { Id = 1, Nombre = "Barraca Carmelo S.A.", Activo = true };
    private static readonly FuenteFinanciamiento Figm = new() { Id = 1, Nombre = "FIGM Literal B", Activo = true, Caja = Caja.Local };
    private static readonly FuenteFinanciamiento PoaObras = new() { Id = 4, Nombre = "POA Obras", Activo = true, Caja = Caja.Poa };
    private static readonly LineaPoa Prensa = new() { Id = 5, Nombre = "PRENSA", Programa = "Comunicación", Ejercicio = 2026, Activo = true };
    private static readonly RubroGasto Materiales = new() { Id = 1, Codigo = 10, Nombre = "Materiales de construcción", Activo = true };

    private static readonly Proyecto RamblaSur = new() { Id = 10, Titulo = "Rambla Sur", Estado = EstadoProyecto.EnCurso };

    private static Gasto Existente(bool activo = true, CondicionPago condicion = CondicionPago.Contado)
    {
        var gasto = new Gasto
        {
            Id = 4, ProveedorId = 1, Proveedor = Barraca, NumeroFactura = "A-0004521", Detalle = "Materiales para bacheo",
            Fecha = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), MontoTotal = 186_540m,
            RubroGastoId = 1, RubroGasto = Materiales,
            CondicionPago = condicion,
            FechaVencimiento = condicion == CondicionPago.Credito ? new DateTime(2030, 12, 31, 0, 0, 0, DateTimeKind.Utc) : null,
            Activo = activo,
        };
        if (condicion == CondicionPago.Contado)
            gasto.Pagos.Add(PagoAutomaticoDePrueba.Con(Figm, fecha: gasto.Fecha, monto: gasto.MontoTotal));
        return gasto;
    }

    private static AdjuntoServiceFake ConAdjuntos(int cantidad)
    {
        var adjuntos = new AdjuntoServiceFake();
        for (var i = 1; i <= cantidad; i++)
            adjuntos.Items.Add(new AdjuntoDto(i, $"factura-A-000452{i}.pdf", "application/pdf", 182_400, 4, null,
                new DateTime(2026, 10, 3, 15, 20, 0, DateTimeKind.Utc)));
        return adjuntos;
    }

    private static SesionFake ConPermiso() => new(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarGastos);
    private static SesionFake SoloVer() => new(RolUsuario.Operador, Permisos.VerFinanzas);

    private sealed record Montaje(Window Ventana, GastoFormView Vista, GastoFormViewModel Vm, GastosFake Servicio, NavigationRecorderFake Navegacion);

    private static GastoFormViewModel NuevoVm(GastosFake servicio, ICurrentSession sesion, INavigationService navegacion,
        ProveedoresFake? proveedores = null, AdjuntoServiceFake? adjuntos = null,
        IReadOnlyList<FuenteFinanciamiento>? fuentes = null)
    {
        var confirmacion = new ConfirmacionServiceFake();
        var panel = new AdjuntosPanelViewModel(adjuntos ?? new AdjuntoServiceFake(), new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(), confirmacion, sesion);
        return new GastoFormViewModel(servicio, sesion, proveedores ?? new ProveedoresFake { Activos = [Barraca] },
            new FuenteFinanciamientoServiceFake(fuentes ?? [Figm, PoaObras]), new RubroGastoServiceFake([Materiales]),
            new LineaPoaServiceFake([Prensa]), new ProyectoServiceFake(new List<Proyecto> { RamblaSur }), navegacion, confirmacion, panel);
    }

    private static Montaje Montar(Action<GastoFormViewModel>? preparar = null, ICurrentSession? sesion = null,
        double ancho = 1040, double alto = 760, ProveedoresFake? proveedores = null, AdjuntoServiceFake? adjuntos = null,
        GastosFake? servicio = null, IReadOnlyList<FuenteFinanciamiento>? fuentes = null)
    {
        servicio ??= new GastosFake();
        var navegacion = new NavigationRecorderFake();
        var vm = NuevoVm(servicio, sesion ?? ConPermiso(), navegacion, proveedores, adjuntos, fuentes);
        preparar?.Invoke(vm);
        var vista = new GastoFormView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        ventana.DataTemplates.Add(new ViewLocator()); // el ContentControl del panel de adjuntos resuelve su View por el ViewLocator de la app
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion);
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

    private static void Completar(GastoFormViewModel vm)
    {
        vm.ProveedorSeleccionado = Barraca;
        vm.Detalle = "Materiales para bacheo";
        vm.MontoTexto = "186.540,00";
        vm.CajaSeleccionada = Caja.Local;
        vm.FuenteSeleccionada = Figm;
        vm.RubroSeleccionado = Materiales;
        Dispatcher.UIThread.RunJobs();
    }

    // ── Estructura ──────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Raiz_TieneUi2_BreadcrumbConElTituloDelVm_EnLosTresModos()
    {
        var alta = Montar();
        var encabezado = Parte<EncabezadoPagina>(alta, "Encabezado");

        Assert.Contains("ui2", alta.Vista.Classes);
        Assert.Equal("Gastos y facturas", encabezado.BreadcrumbOrigen);
        Assert.Equal("Nuevo gasto", encabezado.BreadcrumbActual);
        Assert.Equal("Nuevo gasto", encabezado.Titulo);
        Assert.Same(alta.Vm.CancelarCommand, encabezado.BreadcrumbComando);
        Assert.Equal("Editar gasto", Parte<EncabezadoPagina>(Montar(vm => vm.CargarParaEditar(Existente())), "Encabezado").BreadcrumbActual);
        Assert.Equal("Asociar factura a la entrada",
            Parte<EncabezadoPagina>(Montar(vm => vm.CargarDesdeEntrada(61, 186_540m)), "Encabezado").BreadcrumbActual);
    }

    [AvaloniaFact]
    public void Breadcrumb_ClickReal_VuelveAGastosSinGuardar()
    {
        var m = Montar();
        Completar(m.Vm);
        var origen = Parte<EncabezadoPagina>(m, "Encabezado").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PART_BreadcrumbOrigen");

        Clickear(m.Ventana, origen);

        Assert.Equal(typeof(GastosViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.Empty(m.Servicio.Altas);
    }

    [AvaloniaTheory]
    [InlineData(1040.0)]
    [InlineData(1126.0)]
    public void Tarjeta_Mide760YQuedaALaIzquierda(double ancho)
    {
        var m = Montar(ancho: ancho);
        var tarjeta = EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta"));

        Assert.Equal(760, tarjeta.Width);
        Assert.Equal(28, tarjeta.X);
    }

    [AvaloniaTheory]
    [InlineData(1040.0)]
    [InlineData(1126.0)]
    public void Encabezado_SeLimitaALaColumnaDeLaCard_LasInsigniasTerminanEnSuBorde(double ancho)
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente(activo: false)), ancho: ancho);
        var tarjeta = EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta"));
        var encabezado = EnVentana(m.Ventana, Parte<EncabezadoPagina>(m, "Encabezado"));
        var insignia = EnVentana(m.Ventana, Parte<Insignia>(m, "InsigniaSoloLectura"));

        Assert.Equal(tarjeta.Right, encabezado.Right, 1);
        Assert.Equal(tarjeta.Right, insignia.Right, 1);
    }

    [AvaloniaFact]
    public void Secciones_ConSuTituloEnOrden()
    {
        var m = Montar();

        var titulos = Parte<StackPanel>(m, "Campos").GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("subtitulo-formulario")).Select(t => t.Text).ToList();

        Assert.Equal(new[] { "Factura", "Detalle", "Importe e imputación", "Condición de pago" }, titulos);
    }

    [AvaloniaFact]
    public void Campos_DeAPares_DetalleYDestinoATodoElAncho()
    {
        var m = Montar();
        Rect R(string nombre) => EnVentana(m.Ventana, Parte<CampoFormulario>(m, nombre));

        Assert.Equal(R("CampoProveedor").Y, R("CampoNumeroFactura").Y);
        Assert.Equal(R("CampoProveedor").Right + 16, R("CampoNumeroFactura").X, 1);
        Assert.Equal(R("CampoOrdenCompra").Y, R("CampoFecha").Y);
        Assert.Equal(R("CampoMonto").Y, R("CampoRubro").Y);
        Assert.Equal(R("CampoCaja").Y, R("CampoFuente").Y);
        Assert.Equal(R("CampoMonto").X, R("CampoCaja").X);
        Assert.Equal(R("CampoRubro").X, R("CampoFuente").X);
        Assert.True(R("CampoCaja").Y > R("CampoMonto").Bottom);
        Assert.Equal(R("CampoProveedor").X, R("CampoDetalle").X);
        Assert.Equal(R("CampoNumeroFactura").Right, R("CampoDetalle").Right, 1);
        Assert.Equal(R("CampoDetalle").Width, R("CampoDestino").Width, 1);
        Assert.True(R("CampoDestino").Y > R("CampoDetalle").Bottom);
    }

    // ── Caja y literal del pago de contado (explícitos, sin preselección) ───────

    [AvaloniaFact]
    public void Contado_MuestraCajaYFuente_SinPreseleccion()
    {
        var m = Montar();

        Assert.True(Visible(Parte<CampoFormulario>(m, "CampoCaja")));
        Assert.True(Visible(Parte<CampoFormulario>(m, "CampoFuente")));
        Assert.Null(Parte<ComboBox>(m, "ComboCaja").SelectedItem);
        Assert.Empty(Parte<ComboBox>(m, "ComboFuente").Items);
        Assert.Equal(new[] { Caja.Local, Caja.Poa }, Parte<ComboBox>(m, "ComboCaja").Items.Cast<Caja>().ToArray());
    }

    [AvaloniaFact]
    public void Credito_OcultaCajaYFuente()
    {
        var m = Montar();

        m.Vm.EsCredito = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Parte<CampoFormulario>(m, "CampoCaja")));
        Assert.False(Visible(Parte<CampoFormulario>(m, "CampoFuente")));
    }

    [AvaloniaFact]
    public void LineaPoa_SoloVisibleConContadoYCajaPoa()
    {
        var m = Montar();
        var linea = Parte<CampoFormulario>(m, "CampoLineaPoa");
        Assert.False(Visible(linea));                                            // contado sin caja

        Parte<ComboBox>(m, "ComboCaja").SelectedItem = Caja.Local;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Visible(linea));                                            // contado Local

        Parte<ComboBox>(m, "ComboCaja").SelectedItem = Caja.Poa;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Visible(linea));                                             // contado POA

        m.Vm.EsCredito = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Visible(linea));                                            // crédito
    }

    [AvaloniaFact]
    public void CajaPoa_ElComboDeFuenteSoloTieneLiteralesPoa()
    {
        var m = Montar();
        var fuente = Parte<ComboBox>(m, "ComboFuente");

        Parte<ComboBox>(m, "ComboCaja").SelectedItem = Caja.Poa;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { PoaObras }, fuente.Items.Cast<FuenteFinanciamiento>().ToArray());
        Assert.False(Visible(Parte<Alerta>(m, "AlertaSinLiterales")));
    }

    [AvaloniaFact]
    public void CajaPoaSinLiterales_AlertaVisibleConElTexto()
    {
        var m = Montar(fuentes: [Figm]);

        Parte<ComboBox>(m, "ComboCaja").SelectedItem = Caja.Poa;
        Dispatcher.UIThread.RunJobs();

        var alerta = Parte<Alerta>(m, "AlertaSinLiterales");
        Assert.True(Visible(alerta));
        Assert.Equal("No hay literales asignados a la caja POA; configuralos en Fuentes de financiamiento", alerta.Texto);
        Assert.Equal(VarianteAlerta.Advertencia, alerta.Variante);
    }

    [AvaloniaFact]
    public void Alta_SinCaja_GuardarDeshabilitado()
    {
        var m = Montar();
        Completar(m.Vm);
        Assert.True(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
        m.Vm.CajaSeleccionada = null;
        m.Vm.FuenteSeleccionada = Figm;   // todo lo demás válido: solo la caja falta
        Dispatcher.UIThread.RunJobs();

        Assert.False(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void SalirDeLaCajaSinElegir_MensajeEnElCombo()
    {
        var m = Montar();
        var combo = Parte<ComboBox>(m, "ComboCaja");
        combo.Focus();
        Dispatcher.UIThread.RunJobs();

        Parte<TextBox>(m, "CajaMonto").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { GastoFormViewModel.MensajeCajaObligatoria }, Errores(combo));
    }

    [AvaloniaFact]
    public void Guardar_ClickReal_ContadoPoa_EnviaLaCajaElLiteralYLaLinea()
    {
        var m = Montar();
        Completar(m.Vm);
        Parte<ComboBox>(m, "ComboCaja").SelectedItem = Caja.Poa;
        Dispatcher.UIThread.RunJobs();
        Parte<ComboBox>(m, "ComboFuente").SelectedItem = PoaObras;
        Parte<ComboBox>(m, "ComboLineaPoa").SelectedItem = Prensa;
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        Assert.Equal(new PagoContadoDto(Caja.Poa, 4), Assert.Single(m.Servicio.PagosContado));
        Assert.Equal(5, Assert.Single(m.Servicio.Altas).LineaPoaId);
    }

    [AvaloniaFact]
    public void Proyecto_OperadorSinProyectosVer_ComboExisteEsEditableYGuardaElProyecto()
    {
        // ConPermiso(): Operador con finanzas.ver + finanzas.gastos, SIN proyectos.ver: ListarOpciones solo pide sesión.
        var m = Montar();
        Completar(m.Vm);
        var combo = Parte<ComboBox>(m, "ComboProyecto");

        Assert.True(Visible(Parte<CampoFormulario>(m, "CampoProyecto")));
        Assert.True(combo.IsEffectivelyEnabled);
        Assert.Equal(new[] { "Sin proyecto", "Rambla Sur" },
            combo.Items.Cast<StockApp.Presentation.ViewModels.Proyectos.OpcionProyectoImputacion>().Select(o => o.Nombre));
        Assert.Null(((StockApp.Presentation.ViewModels.Proyectos.OpcionProyectoImputacion)combo.SelectedItem!).Id);

        combo.SelectedItem = m.Vm.ProyectosDisponibles.Single(o => o.Id == 10);
        Dispatcher.UIThread.RunJobs();
        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        Assert.Equal(10, Assert.Single(m.Servicio.Altas).ProyectoId);
    }

    [AvaloniaFact]
    public void Guardar_ClickReal_Credito_NoMandaCajaNiLiteral()
    {
        var m = Montar();
        Completar(m.Vm);
        m.Vm.EsCredito = true;
        m.Vm.FechaVencimientoSeleccionada = new DateTime(2030, 12, 31);
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        Assert.Null(Assert.Single(m.Servicio.PagosContado));
    }

    [AvaloniaFact]
    public void Edicion_ContadoPoa_MuestraCajaLiteralYLinea()
    {
        var gasto = Existente();
        gasto.Pagos.Clear();
        gasto.Pagos.Add(PagoAutomaticoDePrueba.Con(PoaObras, caja: Caja.Poa, fecha: gasto.Fecha, monto: gasto.MontoTotal));
        gasto.LineaPoaId = 5;
        gasto.LineaPoa = Prensa;
        var m = Montar(vm => vm.CargarParaEditar(gasto));

        Assert.Equal(Caja.Poa, Parte<ComboBox>(m, "ComboCaja").SelectedItem);
        Assert.Same(PoaObras, Parte<ComboBox>(m, "ComboFuente").SelectedItem);
        Assert.True(Visible(Parte<CampoFormulario>(m, "CampoLineaPoa")));
        Assert.Same(Prensa, Parte<ComboBox>(m, "ComboLineaPoa").SelectedItem);
    }

    [AvaloniaFact]
    public void Edicion_PagoLocalConLiteralPoa_ElComboLoTieneElegido_YGuardarClickLoManda()
    {
        var gasto = Existente();
        gasto.Pagos.Clear();
        gasto.Pagos.Add(PagoAutomaticoDePrueba.Con(PoaObras, caja: Caja.Local, fecha: gasto.Fecha, monto: gasto.MontoTotal));
        var m = Montar(vm => vm.CargarParaEditar(gasto));

        Assert.Equal(Caja.Local, Parte<ComboBox>(m, "ComboCaja").SelectedItem);
        Assert.Same(PoaObras, Parte<ComboBox>(m, "ComboFuente").SelectedItem);
        Assert.True(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        Assert.Equal(new PagoContadoDto(Caja.Local, 4), Assert.Single(m.Servicio.PagosContado));
    }

    [AvaloniaFact]
    public void ContadoACreditoYDeVuelta_ClickReal_CajaYLiteralVuelvenSinElegir_YGuardarDeshabilitado()
    {
        var m = Montar(alto: 1600);   // alto para que el tilde quede dentro de la ventana, sin scroll
        Completar(m.Vm);
        Assert.True(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);

        Clickear(m.Ventana, Parte<CheckBox>(m, "CheckCredito"));
        Clickear(m.Ventana, Parte<CheckBox>(m, "CheckCredito"));

        Assert.False(m.Vm.EsCredito);
        Assert.Null(m.Vm.CajaSeleccionada);
        Assert.Null(Parte<ComboBox>(m, "ComboCaja").SelectedItem);
        Assert.Null(Parte<ComboBox>(m, "ComboFuente").SelectedItem);
        Assert.False(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
    }

    private static Gasto LegadoLocalConLinea()
    {
        var gasto = Existente();
        gasto.LineaPoaId = 5;
        gasto.LineaPoa = Prensa;
        return gasto;
    }

    [AvaloniaFact]
    public void Edicion_LegadoLocalConLinea_MuestraLaLineaEnSoloLectura_ConSuTextoYElBotonQuitar()
    {
        var m = Montar(vm => vm.CargarParaEditar(LegadoLocalConLinea()));

        Assert.True(Visible(Parte<StackPanel>(m, "BloqueLineaHuerfana")));
        Assert.False(Visible(Parte<CampoFormulario>(m, "CampoLineaPoa")));     // el combo normal no aparece
        var combo = Parte<ComboBox>(m, "ComboLineaHuerfana");
        Assert.False(combo.IsEnabled);
        Assert.Same(Prensa, combo.SelectedItem);
        var alerta = Parte<Alerta>(m, "AlertaLineaHuerfana");
        Assert.Equal("Este gasto tiene una línea POA que ya no corresponde a su caja. Se conserva tal cual.", alerta.Texto);
        Assert.Equal(VarianteAlerta.Advertencia, alerta.Variante);
        var boton = Parte<Button>(m, "BotonQuitarLinea");
        Assert.True(Visible(boton));
        Assert.Equal("Quitar línea", boton.Content);
        Assert.True(boton.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Edicion_LegadoLocalConLinea_QuitarLineaClickReal_OcultaElBloque_YGuardarMandaNull()
    {
        var m = Montar(vm => vm.CargarParaEditar(LegadoLocalConLinea()), alto: 1600);   // sin scroll: el botón queda a la vista

        Clickear(m.Ventana, Parte<Button>(m, "BotonQuitarLinea"));

        Assert.False(Visible(Parte<StackPanel>(m, "BloqueLineaHuerfana")));
        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));
        Assert.Null(Assert.Single(m.Servicio.Modificaciones).LineaPoaId);
    }

    [AvaloniaFact]
    public void Edicion_LegadoLocalConLinea_SinQuitar_GuardarReenviaLaLinea()
    {
        var m = Montar(vm => vm.CargarParaEditar(LegadoLocalConLinea()));

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        Assert.Equal(5, Assert.Single(m.Servicio.Modificaciones).LineaPoaId);
    }

    [AvaloniaFact]
    public void Edicion_LegadoLocalConLinea_PasarACajaPoa_VuelveElComboNormalEditable()
    {
        var m = Montar(vm => vm.CargarParaEditar(LegadoLocalConLinea()));

        Parte<ComboBox>(m, "ComboCaja").SelectedItem = Caja.Poa;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Parte<StackPanel>(m, "BloqueLineaHuerfana")));
        Assert.True(Visible(Parte<CampoFormulario>(m, "CampoLineaPoa")));
        Assert.Same(Prensa, Parte<ComboBox>(m, "ComboLineaPoa").SelectedItem);
        Assert.True(Parte<ComboBox>(m, "ComboLineaPoa").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Edicion_ContadoPoaConLinea_NoMuestraElBloqueHuerfano()
    {
        var gasto = Existente();
        gasto.Pagos.Clear();
        gasto.Pagos.Add(PagoAutomaticoDePrueba.Con(PoaObras, caja: Caja.Poa, fecha: gasto.Fecha, monto: gasto.MontoTotal));
        gasto.LineaPoaId = 5;
        gasto.LineaPoa = Prensa;
        var m = Montar(vm => vm.CargarParaEditar(gasto));

        Assert.False(Visible(Parte<StackPanel>(m, "BloqueLineaHuerfana")));
    }

    [AvaloniaFact]
    public void Vencimiento_SoloACredito()
    {
        var m = Montar();
        Assert.False(Visible(Parte<CampoFormulario>(m, "CampoVencimiento")));

        // La sección Condición de pago queda bajo el pliegue de 760 de alto: se la trae a la vista antes de clickear.
        Parte<CheckBox>(m, "CheckCredito").BringIntoView();
        Clickear(m.Ventana, Parte<CheckBox>(m, "CheckCredito"));

        Assert.True(m.Vm.EsCredito);
        Assert.True(Visible(Parte<CampoFormulario>(m, "CampoVencimiento")));
    }

    // ── Errores y Guardar ───────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AlAbrir_NadaEnRojo_YTocarDetalleVacio_ElErrorVaDebajoDelCampo()
    {
        var m = Montar();
        Assert.Empty(Errores(Parte<TextBox>(m, "CajaDetalle")));

        m.Vm.MarcarTocado(nameof(GastoFormViewModel.Detalle));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { GastoFormViewModel.MensajeDetalleObligatorio }, Errores(Parte<TextBox>(m, "CajaDetalle")));
    }

    [AvaloniaFact]
    public void SalirDeDetalleSinEscribir_MuestraSuError()
    {
        var m = Montar();
        Parte<TextBox>(m, "CajaDetalle").Focus();
        Dispatcher.UIThread.RunJobs();

        Parte<TextBox>(m, "CajaMonto").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { GastoFormViewModel.MensajeDetalleObligatorio }, Errores(Parte<TextBox>(m, "CajaDetalle")));
    }

    [AvaloniaFact]
    public void MontoConPuntoDecimal_ErrorDebajoDelCampo_YGuardarBloqueado()
    {
        var m = Montar();
        Completar(m.Vm);

        Parte<TextBox>(m, "CajaMonto").Text = "5.4";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "El monto total no es un número válido. Usá coma para los decimales: 5,4" },
            Errores(Parte<TextBox>(m, "CajaMonto")));
        Assert.False(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Guardar_DeshabilitadoHastaCompletarLosObligatorios()
    {
        var m = Montar();
        Assert.False(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);

        Completar(m.Vm);

        Assert.True(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
        Assert.Contains("primario", Parte<Button>(m, "BotonGuardar").Classes);
        Assert.Contains("secundario", Parte<Button>(m, "BotonCancelar").Classes);
    }

    [AvaloniaFact]
    public void ErrorDelServidor_EnLaAlertaDeArribaDelPie_FueraDelScroll()
    {
        var servicio = new GastosFake { FallaAlGuardar = new ReglaDeNegocioException("El proveedor 'Barraca Carmelo S.A.' está dado de baja.") };
        var m = Montar(servicio: servicio);
        Completar(m.Vm);

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        var alerta = Parte<Alerta>(m, "AlertaError");
        Assert.True(Visible(alerta));
        Assert.Equal("El proveedor 'Barraca Carmelo S.A.' está dado de baja.", alerta.Texto);
        Assert.False(Parte<ScrollViewer>(m, "Desplazamiento").IsVisualAncestorOf(alerta));
        Assert.True(EnVentana(m.Ventana, alerta).Bottom <= EnVentana(m.Ventana, Parte<Border>(m, "Pie")).Y + 0.5);
        Assert.Equal(EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta")).Width, EnVentana(m.Ventana, alerta).Width, 1);
    }

    [AvaloniaTheory]
    [InlineData(1040.0, 760.0)]
    [InlineData(1126.0, 768.0)]
    public void Pie_FueraDelScroll_ConElFormularioScrolleado_GuardarALaVista(double ancho, double alto)
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente()), ancho: ancho, alto: alto, adjuntos: ConAdjuntos(4));
        var scroll = Parte<ScrollViewer>(m, "Desplazamiento");
        var pie = Parte<Border>(m, "Pie");

        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();

        Assert.False(scroll.IsVisualAncestorOf(pie), "El pie quedó dentro del scroll.");
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 0.5, "El formulario no scrollea: el pie fijo no se está probando.");
        Assert.Equal(m.Vista.Bounds.Height, EnVentana(m.Ventana, pie).Bottom, 0.5);
        Assert.True(EnVentana(m.Ventana, scroll).Bottom <= EnVentana(m.Ventana, pie).Y + 0.5, "El scroll se mete debajo del pie.");
        var guardar = EnVentana(m.Ventana, Parte<Button>(m, "BotonGuardar"));
        Assert.True(guardar.Y >= 0 && guardar.Bottom <= alto + 0.5, "Guardar quedó fuera de la ventana.");
        var tarjeta = EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta"));
        Assert.Equal(tarjeta.Right, guardar.Right, 1);
    }

    // ── Condición de pago ───────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Edicion_TildeACreditoDeshabilitado_ConSuTexto_YElVencimientoALaVista()
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente(condicion: CondicionPago.Credito)));

        var tilde = Parte<CheckBox>(m, "CheckCredito");
        Assert.True(tilde.IsChecked);
        Assert.False(tilde.IsEffectivelyEnabled);
        var texto = Parte<TextBlock>(m, "TextoCondicionFija");
        Assert.True(Visible(texto));
        Assert.Equal(GastoFormViewModel.TextoCondicionPagoFija, texto.Text);
        Assert.True(Visible(Parte<CampoFormulario>(m, "CampoVencimiento")));
    }

    [AvaloniaFact]
    public void AltaYDesdeEntrada_TildeHabilitado_SinElTexto()
    {
        foreach (var m in new[] { Montar(), Montar(vm => vm.CargarDesdeEntrada(61, 186_540m)) })
        {
            Assert.True(Parte<CheckBox>(m, "CheckCredito").IsEffectivelyEnabled);
            Assert.False(Visible(Parte<TextBlock>(m, "TextoCondicionFija")));
        }
    }

    // ── Solo lectura, anulado y adjuntos ────────────────────────────────────────

    [AvaloniaFact]
    public void SinRegistrarGastos_CamposDeshabilitados_GuardarOculto_InsigniaSoloLectura_YVerSigue()
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente()), sesion: SoloVer(), adjuntos: ConAdjuntos(1));

        Assert.True(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        Assert.False(Visible(Parte<Insignia>(m, "InsigniaAnulado")));
        Assert.False(Parte<TextBox>(m, "CajaDetalle").IsEffectivelyEnabled);
        Assert.False(Parte<ComboBox>(m, "ComboProveedor").IsEffectivelyEnabled);
        Assert.False(Parte<CalendarDatePicker>(m, "SelectorFecha").IsEffectivelyEnabled);
        Assert.False(Visible(Parte<Button>(m, "BotonGuardar")));
        Assert.True(Visible(Parte<Button>(m, "BotonCancelar")));
        var panel = m.Vista.GetVisualDescendants().OfType<AdjuntosPanelView>().Single();
        var ver = panel.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Ver");
        Assert.True(Visible(ver));
        Assert.True(ver.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void GastoAnulado_SoloLecturaConInsigniasYAviso_SinLaSeccionAdjuntos()
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente(activo: false)), adjuntos: ConAdjuntos(1));

        Assert.True(Visible(Parte<Insignia>(m, "InsigniaAnulado")));
        Assert.True(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        Assert.True(Visible(Parte<Alerta>(m, "AlertaAnulado")));
        Assert.Equal("Este gasto está anulado: se puede consultar, pero no modificar.", Parte<Alerta>(m, "AlertaAnulado").Texto);
        Assert.False(Visible(Parte<StackPanel>(m, "SeccionAdjuntos")));
        Assert.False(Parte<TextBox>(m, "CajaDetalle").IsEffectivelyEnabled);
        Assert.False(Visible(Parte<Button>(m, "BotonGuardar")));
    }

    [AvaloniaFact]
    public void Alta_SinPanel_ConElAvisoDeGuardarParaAdjuntar()
    {
        var m = Montar();

        var aviso = Parte<TextBlock>(m, "TextoGuardarParaAdjuntar");
        Assert.True(Visible(aviso));
        Assert.Equal("Guardá el gasto para poder adjuntar.", aviso.Text);
        Assert.False(Visible(Parte<ContentControl>(m, "PanelAdjuntos")));
    }

    [AvaloniaFact]
    public void Edicion_ConElPanelDeAdjuntos()
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente()), adjuntos: ConAdjuntos(1));

        Assert.True(Visible(Parte<ContentControl>(m, "PanelAdjuntos")));
        Assert.Single(m.Vista.GetVisualDescendants().OfType<AdjuntosPanelView>());
        Assert.False(Visible(Parte<TextBlock>(m, "TextoGuardarParaAdjuntar")));
        Assert.False(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        Assert.False(Visible(Parte<Insignia>(m, "InsigniaAnulado")));
    }

    [AvaloniaFact]
    public void AltaYEdicionVigente_SinAvisosNiEstadosDeExcepcion_SoloLaCard()
    {
        foreach (var m in new[] { Montar(), Montar(vm => vm.CargarParaEditar(Existente())) })
        {
            Assert.True(Visible(Parte<TarjetaContenido>(m, "Tarjeta")));
            Assert.False(Visible(Parte<EstadoVacio>(m, "EstadoSinPermiso")));
            Assert.False(Visible(Parte<Alerta>(m, "AlertaAnulado")));
            Assert.False(Visible(Parte<Alerta>(m, "AlertaError")));
            Assert.False(Visible(Parte<Alerta>(m, "AlertaSinConexion")));
        }
    }

    // ── Carga inicial ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task CargaInicialSinServidor_AlertaConReintentar_ClickRealCargaLosCombos()
    {
        var proveedores = new ProveedoresFake { Activos = [Barraca], Falla = new ServidorNoDisponibleException() };
        var m = Montar(proveedores: proveedores);
        Assert.True(Visible(Parte<Alerta>(m, "AlertaSinConexion")));
        Assert.True(Visible(Parte<TarjetaContenido>(m, "Tarjeta")));

        proveedores.Falla = null;
        Clickear(m.Ventana, Parte<Button>(m, "BotonReintentar"));
        await (m.Vm.ReintentarCargaCommand.ExecutionTask ?? Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Parte<Alerta>(m, "AlertaSinConexion")));
        Assert.Single(m.Vm.ProveedoresDisponibles);
    }

    [AvaloniaFact]
    public void CargaInicialCon403_SinPermisoEnLugarDeLaCard_YEnElPieSoloCancelar()
    {
        var m = Montar(proveedores: new ProveedoresFake { Falla = new UnauthorizedAccessException("403") });

        var estado = Parte<EstadoVacio>(m, "EstadoSinPermiso");
        Assert.True(Visible(estado));
        Assert.Equal(GastoFormViewModel.MensajeSinPermisoFormulario, estado.Mensaje);
        Assert.False(Visible(Parte<TarjetaContenido>(m, "Tarjeta")));
        Assert.False(Visible(Parte<Button>(m, "BotonGuardar")));
        Assert.True(Visible(Parte<Button>(m, "BotonCancelar")));
    }

    // ── Revocación en caliente dentro del shell ─────────────────────────────────

    [AvaloniaFact]
    public async Task RevocacionDeRegistrarGastos_DentroDelShell_PasaASoloLecturaSinBorrarLoTipeado()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarGastos);
        var tras = new HashSet<string> { Permisos.VerFinanzas };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = NuevoVm(new GastosFake(), sesion, new NavigationRecorderFake());
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
        vm.Detalle = "Materiales de obra";
        Dispatcher.UIThread.RunJobs();
        var vista = ventana.GetVisualDescendants().OfType<GastoFormView>().Single();
        Assert.True(MontajeV2.Parte<TextBox>(vista, "CajaDetalle").IsEffectivelyEnabled, "Precondición: con finanzas.gastos se edita.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.False(MontajeV2.Parte<TextBox>(vista, "CajaDetalle").IsEffectivelyEnabled);
        Assert.True(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<Insignia>(vista, "InsigniaSoloLectura")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<Button>(vista, "BotonGuardar")));
        Assert.Equal("Materiales de obra", MontajeV2.Parte<TextBox>(vista, "CajaDetalle").Text);
    }
}
