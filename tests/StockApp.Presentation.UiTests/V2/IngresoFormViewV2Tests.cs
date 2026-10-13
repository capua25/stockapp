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
using StockApp.Application.Authorization;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Formulario de ingreso v2 con la View REAL: estructura del patrón ProductoFormView, errores por campo debajo
/// de cada control (nada en rojo al abrir), Alerta del servidor y del 403, gates con un Operador mixto y 403 en caliente.</summary>
public class IngresoFormViewV2Tests
{
    private static readonly FuenteFinanciamiento Figm = new() { Id = 1, Nombre = "FIGM Literal B", Activo = true };
    private static readonly FuenteFinanciamiento DeBaja = new() { Id = 9, Nombre = "Fondo de emergencia", Activo = false };

    private static readonly IngresoCaja Existente = new()
    {
        Id = 7, Fecha = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), Concepto = "Multas de tránsito setiembre",
        FuenteFinanciamientoId = 1, FuenteFinanciamiento = Figm, Monto = 48_300m, Activo = true,
    };

    private static SesionFake ConPermiso() => new(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarIngresos);

    private sealed record Montaje(Window Ventana, IngresoFormView Vista, IngresoFormViewModel Vm, IngresoCajaFake Servicio,
        NavigationRecorderFake Navegacion);

    private static Montaje Montar(IngresoCaja? edicion = null, FuentesActivasFake? fuentes = null, SesionFake? sesion = null,
        IngresoCajaFake? servicio = null)
    {
        servicio ??= new IngresoCajaFake();
        var navegacion = new NavigationRecorderFake();
        var vm = new IngresoFormViewModel(servicio, fuentes ?? new FuentesActivasFake { Activas = [Figm] }, sesion ?? ConPermiso(), navegacion);
        if (edicion is not null) vm.CargarParaEditar(edicion);
        var vista = new IngresoFormView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion);
    }

    private static T Parte<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);
    private static Rect EnVentana(Window ventana, Control c) => new(c.TranslatePoint(new Point(0, 0), ventana)!.Value, c.Bounds.Size);

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<string> Errores(Control c) => DataValidationErrors.GetErrors(c)?.Select(e => e?.ToString() ?? "") ?? [];

    private static void Completar(Montaje m)
    {
        m.Vm.FuenteSeleccionada = Figm;
        m.Vm.Concepto = "Partida mensual FIGM octubre";
        m.Vm.MontoTexto = "1.250.000,00";
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Raiz_TieneUi2_YBreadcrumbConElTituloDelVm()
    {
        var alta = Montar();
        var encabezado = Parte<EncabezadoPagina>(alta, "Encabezado");

        Assert.Contains("ui2", alta.Vista.Classes);
        Assert.Equal("Ingresos de caja", encabezado.BreadcrumbOrigen);
        Assert.Equal("Nuevo ingreso", encabezado.BreadcrumbActual);
        Assert.Equal("Nuevo ingreso", encabezado.Titulo);
        Assert.Same(alta.Vm.CancelarCommand, encabezado.BreadcrumbComando);

        Assert.Equal("Editar ingreso", Parte<EncabezadoPagina>(Montar(Existente), "Encabezado").Titulo);
    }

    [AvaloniaFact]
    public void Breadcrumb_ClickReal_VuelveAlListadoSinGuardar()
    {
        var m = Montar();
        Completar(m);
        var origen = Parte<EncabezadoPagina>(m, "Encabezado").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PART_BreadcrumbOrigen");

        Clickear(m.Ventana, origen);

        Assert.Equal(typeof(IngresosViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.Empty(m.Servicio.Altas);
    }

    [AvaloniaFact]
    public void Tarjeta_Mide760YQuedaALaIzquierda()
    {
        var m = Montar();
        var tarjeta = EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta"));

        Assert.Equal(760, tarjeta.Width);
        Assert.Equal(28, tarjeta.X);
    }

    [AvaloniaFact]
    public void Campos_FechaYMontoArriba_ConceptoATodoElAncho_FuenteALaIzquierda()
    {
        var m = Montar();
        var fecha = EnVentana(m.Ventana, Parte<CampoFormulario>(m, "CampoFecha"));
        var monto = EnVentana(m.Ventana, Parte<CampoFormulario>(m, "CampoMonto"));
        var concepto = EnVentana(m.Ventana, Parte<CampoFormulario>(m, "CampoConcepto"));
        var fuente = EnVentana(m.Ventana, Parte<CampoFormulario>(m, "CampoFuente"));

        Assert.Equal(fecha.Y, monto.Y);
        Assert.Equal(fecha.Right + 16, monto.X, 1);
        Assert.Equal(fecha.X, concepto.X);
        Assert.Equal(monto.Right, concepto.Right, 1);
        Assert.True(concepto.Y > fecha.Bottom);
        Assert.Equal(fecha.X, fuente.X);
        Assert.Equal(fecha.Width, fuente.Width, 1);
        Assert.True(fuente.Y > concepto.Bottom);
    }

    [AvaloniaFact]
    public void Requeridos_LosCuatro_YMontoALaDerecha()
    {
        var m = Montar();

        Assert.All(new[] { "CampoFecha", "CampoMonto", "CampoConcepto", "CampoFuente" },
            n => Assert.True(Parte<CampoFormulario>(m, n).Requerido, n));
        Assert.Equal(Avalonia.Media.TextAlignment.Right, Parte<TextBox>(m, "CajaMonto").TextAlignment);
    }

    [AvaloniaFact]
    public void AlAbrir_NingunCampoEnRojo()
    {
        var m = Montar();

        Assert.False(DataValidationErrors.GetHasErrors(Parte<CalendarDatePicker>(m, "SelectorFecha")));
        Assert.False(DataValidationErrors.GetHasErrors(Parte<TextBox>(m, "CajaMonto")));
        Assert.False(DataValidationErrors.GetHasErrors(Parte<TextBox>(m, "CajaConcepto")));
        Assert.False(DataValidationErrors.GetHasErrors(Parte<ComboBox>(m, "ComboFuente")));
        Assert.False(m.Vm.HayErroresDeEntrada);
    }

    [AvaloniaFact]
    public void SalirDelConceptoVacio_MensajeDebajoDelCampo()
    {
        var m = Montar();
        var concepto = Parte<TextBox>(m, "CajaConcepto");

        concepto.Focus();
        Dispatcher.UIThread.RunJobs();
        Parte<TextBox>(m, "CajaMonto").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { IngresoFormViewModel.MensajeConceptoObligatorio }, Errores(concepto));
        var mensaje = concepto.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == IngresoFormViewModel.MensajeConceptoObligatorio);
        Assert.True(Visible(mensaje));
        Assert.True(EnVentana(m.Ventana, mensaje).Y >= EnVentana(m.Ventana, concepto).Y + 36 - 1);
    }

    [AvaloniaFact]
    public void MontoInvalido_MensajeEsUyDebajoDelCampo_YGuardarBloqueado()
    {
        var m = Montar();
        Completar(m);

        Parte<TextBox>(m, "CajaMonto").Text = "1.5";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "El monto no es un número válido. Usá coma para los decimales: 1,5" }, Errores(Parte<TextBox>(m, "CajaMonto")));
        Assert.True(m.Vm.HayErroresDeEntrada);
        Assert.False(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
        Assert.False(Visible(Parte<Alerta>(m, "AlertaError")));
    }

    [AvaloniaFact]
    public void CorregirElCampo_QuitaElErrorYHabilitaGuardar()
    {
        var m = Montar();
        Completar(m);
        var monto = Parte<TextBox>(m, "CajaMonto");
        monto.Text = "1.5";
        Dispatcher.UIThread.RunJobs();

        monto.Text = "1,5";
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(Errores(monto));
        Assert.False(m.Vm.HayErroresDeEntrada);
        Assert.True(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void FechaBorrada_MensajeEnElSelectorDeFecha()
    {
        var m = Montar();

        m.Vm.FechaSeleccionada = null;
        Dispatcher.UIThread.RunJobs();

        var selector = Parte<CalendarDatePicker>(m, "SelectorFecha");
        Assert.Equal(new[] { IngresoFormViewModel.MensajeFechaObligatoria }, Errores(selector));
        // El binding de SelectedDate lee el INotifyDataErrorInfo del VM y el tema dibuja el mensaje debajo de la caja.
        var mensaje = selector.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == IngresoFormViewModel.MensajeFechaObligatoria);
        Assert.True(Visible(mensaje));
        Assert.True(EnVentana(m.Ventana, mensaje).Y >= EnVentana(m.Ventana, selector).Y + 36 - 1);
    }

    /// <summary>Saca el foco de un control: lo pasa a Concepto o, si el que sale es Concepto, a Monto.</summary>
    private static void EnfocarYSalir(Montaje m, Control control)
    {
        control.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(control.IsKeyboardFocusWithin, "Precondición: el control tiene el foco.");
        var otro = control.Name == "CajaConcepto" ? Parte<TextBox>(m, "CajaMonto") : Parte<TextBox>(m, "CajaConcepto");
        otro.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void SalirDelMontoVacio_MensajeObligatorioDebajoDelCampo()
    {
        var m = Montar();
        var monto = Parte<TextBox>(m, "CajaMonto");

        EnfocarYSalir(m, monto);

        Assert.Equal(new[] { IngresoFormViewModel.MensajeMontoObligatorio }, Errores(monto));
    }

    [AvaloniaFact]
    public void SalirDeLaFuenteSinElegir_MensajeEnElCombo()
    {
        var m = Montar();
        var combo = Parte<ComboBox>(m, "ComboFuente");

        EnfocarYSalir(m, combo);

        Assert.Equal(new[] { IngresoFormViewModel.MensajeFuenteObligatoria }, Errores(combo));
        var mensaje = combo.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == IngresoFormViewModel.MensajeFuenteObligatoria);
        Assert.True(Visible(mensaje));
    }

    [AvaloniaFact]
    public void AbrirLaListaDeFuentes_NoMarcaLaFuenteEnRojo()
    {
        var m = Montar();
        var combo = Parte<ComboBox>(m, "ComboFuente");
        combo.Focus();
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, combo);

        Assert.True(combo.IsDropDownOpen, "Precondición: el click abre la lista.");
        Assert.Empty(Errores(combo));
        combo.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void SalirDeLaFecha_LaMarcaComoTocada()
    {
        var m = Montar();
        var tocados = new List<string?>();
        m.Vm.ErrorsChanged += (_, e) => tocados.Add(e.PropertyName);
        var cajaDeFecha = Parte<CalendarDatePicker>(m, "SelectorFecha").GetVisualDescendants().OfType<TextBox>().First();

        EnfocarYSalir(m, cajaDeFecha);

        Assert.Contains(nameof(IngresoFormViewModel.FechaSeleccionada), tocados);
    }

    [AvaloniaFact]
    public void ConceptoTocadoYCorregido_ElMensajeDesapareceDeLaVista()
    {
        var m = Montar();
        var concepto = Parte<TextBox>(m, "CajaConcepto");
        EnfocarYSalir(m, concepto);
        Assert.Contains(concepto.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == IngresoFormViewModel.MensajeConceptoObligatorio && Visible(t));

        concepto.Text = "Multas de tránsito";
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(Errores(concepto));
        Assert.DoesNotContain(concepto.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == IngresoFormViewModel.MensajeConceptoObligatorio && Visible(t));
    }

    [AvaloniaFact]
    public void Guardar_ClickReal_ConDatosValidos_DaDeAltaYVuelveAlListado()
    {
        var m = Montar();
        Completar(m);

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        var alta = Assert.Single(m.Servicio.Altas);
        Assert.Equal(1_250_000m, alta.Monto);
        Assert.Equal(typeof(IngresosViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Guardar_ConErrorDelServidor_AlertaArribaDelPie_YNoNavega()
    {
        var m = Montar(servicio: new IngresoCajaFake { FallaAlGuardar = new ArgumentException("El monto del ingreso debe ser mayor a cero.") });
        Completar(m);

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        var alerta = Parte<Alerta>(m, "AlertaError");
        Assert.True(Visible(alerta));
        Assert.Equal("El monto del ingreso debe ser mayor a cero.", alerta.Texto);
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.True(EnVentana(m.Ventana, alerta).Bottom <= EnVentana(m.Ventana, Parte<Border>(m, "PieFormulario")).Y);
        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Pie_CancelarSecundario_GuardarUnicoPrimario()
    {
        var m = Montar();

        Assert.Contains("secundario", Parte<Button>(m, "BotonCancelar").Classes);
        Assert.Contains("primario", Parte<Button>(m, "BotonGuardar").Classes);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario") && Visible(b));
    }

    [AvaloniaFact]
    public void SinPermisoAlInicializar_AlertaYGuardarDeshabilitado()
    {
        var m = Montar(fuentes: new FuentesActivasFake { Falla = new UnauthorizedAccessException("403") });
        Completar(m);

        var alerta = Parte<Alerta>(m, "AlertaSinPermiso");
        Assert.True(Visible(alerta));
        Assert.Equal(IngresoFormViewModel.MensajeSinPermisoFormulario, alerta.Texto);
        Assert.True(Visible(Parte<TarjetaContenido>(m, "Tarjeta")));
        Assert.False(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Gates_OperadorMixto_SinGuardar()
    {
        var m = Montar(sesion: new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas));

        Assert.False(Visible(Parte<Button>(m, "BotonGuardar")));
        Assert.True(Visible(Parte<Button>(m, "BotonCancelar")));
    }

    [AvaloniaFact]
    public void Edicion_ConFuenteDadaDeBaja_LaMuestraElegida()
    {
        var m = Montar(new IngresoCaja
        {
            Id = 8, Fecha = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), Concepto = "Aporte de emergencia",
            FuenteFinanciamientoId = 9, FuenteFinanciamiento = DeBaja, Monto = 10_000m, Activo = true,
        });

        Assert.Same(DeBaja, Parte<ComboBox>(m, "ComboFuente").SelectedItem);
        Assert.Contains(DeBaja, m.Vm.FuentesDisponibles);
    }

    [AvaloniaFact]
    public async Task Un403_QueQuitaRegistrarIngresos_OcultaGuardarSinNavegar()
    {
        var sesion = ConPermiso();
        var tras = new HashSet<string> { Permisos.VerFinanzas };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = new IngresoFormViewModel(new IngresoCajaFake(), new FuentesActivasFake { Activas = [Figm] }, sesion, new NavigationServiceFake());
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
        var vista = ventana.GetVisualDescendants().OfType<IngresoFormView>().Single();
        Assert.True(Visible(MontajeV2.Parte<Button>(vista, "BotonGuardar")), "Precondición: con finanzas.ingresos se ve.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(MontajeV2.Parte<Button>(vista, "BotonGuardar")));
        Assert.Same(vm, shell.CurrentContent);
    }
}
