using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Verificacion inversa Vista-ViewModel + recorrido de uso con clicks reales de TareaFormView
/// (modulo Tareas, 2026-08-01). A diferencia de TareaListView/NuevaImportacionView,
/// TareaFormView NO tiene wiring de DataContextChanged (CargarParaCrear/CargarParaVer son
/// sincronos y corren ANTES de publicar el VM como DataContext -- ver TareaFormView.axaml.cs),
/// asi que estos tests llaman Cargar* directo sobre el VM antes de Show(), igual que hace
/// INavigationService.Navegar&lt;TVm&gt;(Action&lt;TVm&gt;) en produccion.
/// </summary>
public class TareaFormViewTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:tareas="clr-namespace:StockApp.Presentation.Views.Tareas;assembly=GestionMunicipal"
                Width="800" Height="800">
            <tareas:TareaFormView />
        </Window>
        """;

    private static (Window Window, TareaFormViewModel Vm, TareaServiceFake Servicio, NavigationRecorderFake Nav) MontarParaCrear(
        RolUsuario rol = RolUsuario.Admin)
    {
        var servicio = new TareaServiceFake();
        var nav = new NavigationRecorderFake();
        var vm = new TareaFormViewModel(servicio, new SesionFake(rol), nav, new ConfirmacionServiceFake());
        vm.CargarParaCrear();

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja asentar bindings de Command/IsEnabled

        return (window, vm, servicio, nav);
    }

    private static (Window Window, TareaFormViewModel Vm, TareaServiceFake Servicio, NavigationRecorderFake Nav) MontarParaVer(
        Tarea tarea, RolUsuario rol = RolUsuario.Admin, TareaServiceFake? servicioExistente = null)
    {
        var servicio = servicioExistente ?? new TareaServiceFake(new System.Collections.Generic.List<Tarea> { tarea });
        var nav = new NavigationRecorderFake();
        var vm = new TareaFormViewModel(servicio, new SesionFake(rol), nav, new ConfirmacionServiceFake());
        vm.CargarParaVer(tarea);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja asentar bindings de Command/IsEnabled

        return (window, vm, servicio, nav);
    }

    private static void Clickear(Window window, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var centro = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var puntoEnVentana = control.TranslatePoint(centro, window) ?? centro;
        window.MouseMove(puntoEnVentana);
        window.MouseDown(puntoEnVentana, MouseButton.Left);
        window.MouseUp(puntoEnVentana, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button BotonVisiblePorTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, texto) && ArbolVisual.EsVisibleEnArbol(b));

    [AvaloniaFact]
    public void ModoAlta_MuestraTituloDescripcionFechaYBotonesGuardarVolver()
    {
        var (window, vm, _, _) = MontarParaCrear();

        Assert.True(vm.EsNuevaTarea);
        // CalendarDatePicker es un TemplatedControl que trae SU PROPIO TextBox interno (el de
        // "dd/mm/aaaa") -- se excluye para quedarnos solo con los TextBox de autor (Titulo y
        // Descripcion); el picker se verifica aparte mas abajo.
        var textBoxesVisibles = window.GetVisualDescendants().OfType<TextBox>()
            .Where(t => t.FindAncestorOfType<CalendarDatePicker>() is null)
            .Where(ArbolVisual.EsVisibleEnArbol)
            .ToList();
        Assert.Equal(2, textBoxesVisibles.Count); // Titulo + Descripcion

        var fechaPicker = window.GetVisualDescendants().OfType<CalendarDatePicker>().Single();
        Assert.True(ArbolVisual.EsVisibleEnArbol(fechaPicker));

        Assert.True(BotonVisiblePorTexto(window, "Guardar").IsVisible);
        Assert.True(BotonVisiblePorTexto(window, "Volver").IsVisible);
    }

    [AvaloniaFact]
    public void ModoAlta_TituloVacio_ElBotonGuardarQuedaDeshabilitado()
    {
        var (window, vm, _, _) = MontarParaCrear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
        var boton = BotonVisiblePorTexto(window, "Guardar");

        // Hallazgo puntual de esta verificacion: TareaFormView NO bindea IsEnabled del boton
        // "Guardar" explicitamente -- depende del gating automatico de Avalonia via
        // Button.Command.CanExecute. En Avalonia ese gating automatico NO toca la propiedad
        // IsEnabled en si (que queda en su valor por defecto True); el que SI refleja el
        // CanExecute real es IsEffectivelyEnabled (la propiedad efectiva que determina si el
        // click se procesa y si se aplica el pseudo-estado :disabled). Confirmado con
        // NotifyCanExecuteChanged() + RunJobs(): IsEnabled se queda en True, IsEffectivelyEnabled
        // pasa a False -- por eso el assert real tiene que ser sobre IsEffectivelyEnabled.
        Assert.False(boton.IsEffectivelyEnabled);
    }

    /// <summary>
    /// Recorrido completo de ALTA con clicks/tipeo reales: tipear titulo, elegir fecha limite en
    /// el CalendarDatePicker y clickear "Guardar" -- el mismo camino de
    /// TareaFormViewModelTests.GuardarAsync_ConTitulo_CreaLaTareaYVuelveAlListado pero contra la
    /// VISTA real, no invocando el comando a mano.
    /// </summary>
    [AvaloniaFact]
    public void ClickReal_TipearTituloYFecha_ClickearGuardar_CreaLaTareaYNavegaAlListado()
    {
        var (window, vm, servicio, nav) = MontarParaCrear();

        var titulo = window.GetVisualDescendants().OfType<TextBox>().Where(ArbolVisual.EsVisibleEnArbol).First();
        titulo.Focus();
        titulo.Text = "Reparar bache en calle Rivera";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Reparar bache en calle Rivera", vm.Titulo);

        var fechaPicker = window.GetVisualDescendants().OfType<CalendarDatePicker>().Single();
        fechaPicker.SelectedDate = new DateTime(2026, 9, 1);
        Dispatcher.UIThread.RunJobs();

        // Verificacion Vista->ViewModel del binding de fecha: sin Mode=TwoWay explicito en el
        // axaml (igual que GastoFormView/IngresoPorFacturaView, que tampoco lo declaran), si el
        // binding no fuera TwoWay por defecto este assert fallaria ANTES de tocar el boton
        // Guardar -- aislando si el problema es el picker o el comando.
        Assert.Equal(new DateTime(2026, 9, 1), vm.FechaLimiteSeleccionada);

        var botonGuardar = BotonVisiblePorTexto(window, "Guardar");
        Assert.True(botonGuardar.IsEffectivelyEnabled); // ver nota sobre IsEnabled vs IsEffectivelyEnabled mas abajo en este archivo
        Clickear(window, botonGuardar);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(servicio.TareasCreadas);
        Assert.Equal("Reparar bache en calle Rivera", servicio.TareasCreadas[0].Titulo);
        Assert.Equal(new DateTime(2026, 9, 1), servicio.TareasCreadas[0].FechaLimite!.Value.Date);
        Assert.Equal(typeof(TareaListViewModel), nav.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void ModoDetalle_MuestraElHiloDeNotasExistentes()
    {
        var tarea = new Tarea { Id = 5, Titulo = "Bache" };
        tarea.Notas.Add(new NotaTarea { TareaId = 5, Texto = "primera nota", Fecha = DateTime.UtcNow });
        tarea.Notas.Add(new NotaTarea { TareaId = 5, Texto = "segunda nota", Fecha = DateTime.UtcNow });

        var (window, vm, _, _) = MontarParaVer(tarea);

        Assert.Equal(2, vm.Notas.Count);
        var textos = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(ArbolVisual.EsVisibleEnArbol).Select(t => t.Text).ToList();
        Assert.Contains("primera nota", textos);
        Assert.Contains("segunda nota", textos);
    }

    /// <summary>Recorrido real: tipear una nota manual y clickear "Agregar nota" -- confirma que
    /// el TextBox de nota nueva y el boton existen y estan conectados, no solo que
    /// AgregarNotaCommand funciona a nivel VM (ya cubierto por TareaFormViewModelTests).</summary>
    [AvaloniaFact]
    public void ClickReal_TipearNotaYClickearAgregar_SumaLaNotaAlHiloVisible()
    {
        var tarea = new Tarea { Id = 5, Titulo = "Bache" };
        var (window, vm, servicio, _) = MontarParaVer(tarea);

        var cajaNota = window.GetVisualDescendants().OfType<TextBox>().Where(ArbolVisual.EsVisibleEnArbol).Single();
        cajaNota.Focus();
        cajaNota.Text = "avance del dia";
        Dispatcher.UIThread.RunJobs();

        var botonAgregar = BotonVisiblePorTexto(window, "Agregar nota");
        Assert.True(botonAgregar.IsEffectivelyEnabled); // ver nota sobre IsEnabled vs IsEffectivelyEnabled mas arriba en este archivo
        Clickear(window, botonAgregar);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains((5, "avance del dia"), servicio.NotasAgregadas);
        Assert.Single(vm.Notas);
        var textos = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(ArbolVisual.EsVisibleEnArbol).Select(t => t.Text);
        Assert.Contains("avance del dia", textos);
    }

    [AvaloniaFact]
    public void ModoDetalle_AdminConTareaPendiente_MuestraCambioDePrioridad()
    {
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media };
        var (window, vm, _, _) = MontarParaVer(tarea, rol: RolUsuario.Admin);

        Assert.True(vm.MuestraCambioPrioridad);
        var combo = window.GetVisualDescendants().OfType<ComboBox>().Where(ArbolVisual.EsVisibleEnArbol).ToList();
        Assert.Single(combo);
        Assert.True(BotonVisiblePorTexto(window, "Actualizar prioridad").IsVisible);
    }

    /// <summary>
    /// MISMO HUECO DE GATING que en TareaListView, ahora sobre TareaFormView: un Operador viendo
    /// el detalle de una tarea NO debe ver el control de cambio de prioridad (accion solo-Admin
    /// del spec), y esto se verifica contra el arbol visual real, no contra EsAdmin/MuestraCambioPrioridad
    /// a nivel VM (que ya cubre TareaFormViewModelTests indirectamente via CargarParaVer_ComoAdminConTareaTerminada).
    /// </summary>
    [AvaloniaFact]
    public void ModoDetalle_OperadorConTareaPendiente_NoMuestraCambioDePrioridad()
    {
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media };
        var (window, vm, _, _) = MontarParaVer(tarea, rol: RolUsuario.Operador);

        Assert.False(vm.MuestraCambioPrioridad);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComboBox>(), ArbolVisual.EsVisibleEnArbol);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
            b => Equals(b.Content, "Actualizar prioridad") && ArbolVisual.EsVisibleEnArbol(b));
    }

    [AvaloniaFact]
    public void ModoDetalle_AdminConTareaTerminada_NoMuestraCambioDePrioridad()
    {
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Terminada, Prioridad = PrioridadTarea.Media };
        var (window, vm, _, _) = MontarParaVer(tarea, rol: RolUsuario.Admin);

        Assert.False(vm.MuestraCambioPrioridad);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComboBox>(), ArbolVisual.EsVisibleEnArbol);
    }

    [AvaloniaFact]
    public void ClickReal_EnActualizarPrioridad_LlamaAlServicioConLaOpcionElegida()
    {
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media };
        var (window, vm, servicio, _) = MontarParaVer(tarea, rol: RolUsuario.Admin);

        var combo = window.GetVisualDescendants().OfType<ComboBox>().Where(ArbolVisual.EsVisibleEnArbol).Single();
        combo.SelectedItem = PrioridadTarea.Alta;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PrioridadTarea.Alta, vm.PrioridadSeleccionada);

        var boton = BotonVisiblePorTexto(window, "Actualizar prioridad");
        Clickear(window, boton);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains((5, PrioridadTarea.Alta), servicio.CambiosDePrioridad);
    }
}
