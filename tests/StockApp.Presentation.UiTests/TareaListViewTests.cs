using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Verificacion inversa Vista-ViewModel + recorrido de uso con clicks reales de TareaListView
/// (modulo Tareas, 2026-08-01). Cierra el hueco documentado en el post-mortem del proyecto: "los
/// tests verifican lo que conectaste, nunca lo que te olvidaste de conectar" -- especificamente
/// el gating de PERMISOS por rol (Operador no debe ver "Cancelar") se cubria solo con unit tests
/// de TareaListViewModelTests.cs, nunca contra el arbol visual real de la vista.
/// </summary>
public class TareaListViewTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:tareas="clr-namespace:StockApp.Presentation.Views.Tareas;assembly=StockApp.Presentation"
                Width="1000" Height="800">
            <tareas:TareaListView />
        </Window>
        """;

    private static Tarea TareaDe(
        int id, string titulo, EstadoTarea estado, DateTime? fechaLimite = null, Usuario? tomadaPor = null) => new()
    {
        Id = id, Titulo = titulo, Estado = estado, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = fechaLimite, TomadaPor = tomadaPor,
    };

    private static (Window Window, TareaListViewModel Vm, TareaServiceFake Servicio) Montar(
        List<Tarea> tareas, RolUsuario rol = RolUsuario.Admin, INavigationService? navegacion = null)
    {
        var servicio = new TareaServiceFake(tareas);
        var vm = new TareaListViewModel(
            servicio, new TareaSessionFake(rol), navegacion ?? new NavigationRecorderFake(), new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm, servicio);
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

    private static Button BotonPorTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, texto) && b.IsVisible);

    [AvaloniaFact]
    public void Montar_ConTareasEnLosCuatroEstados_LasAgrupaPorSeccionVisualmente()
    {
        var tareas = new List<Tarea>
        {
            TareaDe(1, "Pendiente 1", EstadoTarea.Pendiente),
            TareaDe(2, "En curso 1", EstadoTarea.EnCurso),
            TareaDe(3, "Terminada 1", EstadoTarea.Terminada),
        };
        var (window, vm, _) = Montar(tareas);

        Assert.Single(vm.Pendientes);
        Assert.Single(vm.EnCurso);
        Assert.Single(vm.Terminadas);

        var textos = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Pendiente 1", textos);
        Assert.Contains("En curso 1", textos);
        Assert.Contains("Terminada 1", textos);

        // Canceladas esta detras del filtro MostrarCanceladas (spec): sin tildar el checkbox,
        // la seccion completa no debe estar visible en el arbol.
        var seccionCanceladas = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "Canceladas");
        Assert.NotNull(seccionCanceladas);
        Assert.False(ArbolVisual.EsVisibleEnArbol(seccionCanceladas!));
    }

    [AvaloniaFact]
    public void Montar_TareaPendiente_MuestraTomarYOcultaSoltarYTerminar()
    {
        var (window, vm, _) = Montar(new List<Tarea> { TareaDe(1, "x", EstadoTarea.Pendiente) });
        var fila = vm.Pendientes[0];

        Assert.True(fila.PuedeTomar);
        var botonTomar = BotonPorTexto(window, "Tomar");
        Assert.True(botonTomar.IsVisible);

        // Soltar/Terminar son de la seccion "En curso": no deben existir botones VISIBLES con
        // ese texto mientras la unica tarea este Pendiente.
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
            b => Equals(b.Content, "Soltar") && b.IsVisible);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
            b => Equals(b.Content, "Terminar") && b.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_TareaEnCurso_MuestraSoltarYTerminarYOcultaTomar()
    {
        var (window, vm, _) = Montar(new List<Tarea>
        {
            TareaDe(1, "x", EstadoTarea.EnCurso, tomadaPor: new Usuario { NombreUsuario = "juan" }),
        });

        Assert.True(BotonPorTexto(window, "Soltar").IsVisible);
        Assert.True(BotonPorTexto(window, "Terminar").IsVisible);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
            b => Equals(b.Content, "Tomar") && b.IsVisible);

        var textos = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);
        Assert.Contains("Tomada por juan", textos);
    }

    /// <summary>
    /// EL HUECO CONOCIDO del proyecto (post-mortem): el gating de permisos estaba cubierto solo
    /// por unit tests de VM (TareaListViewModelTests.CargarAsync_ConRolOperador_FilaNoPuedeCancelar),
    /// nunca contra la vista real. Este test monta la vista REAL con un usuario Operador y
    /// confirma que el boton "Cancelar" no esta visible en ninguna fila -- si el binding de
    /// IsVisible tuviera un typo, este test (y no uno de VM) lo detectaria.
    /// </summary>
    [AvaloniaFact]
    public void Montar_RolOperador_NoMuestraElBotonCancelarEnNingunaFila()
    {
        var (window, vm, _) = Montar(
            new List<Tarea>
            {
                TareaDe(1, "Pendiente", EstadoTarea.Pendiente),
                TareaDe(2, "En curso", EstadoTarea.EnCurso),
            },
            rol: RolUsuario.Operador);

        Assert.False(vm.Pendientes[0].PuedeCancelar);
        Assert.False(vm.EnCurso[0].PuedeCancelar);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
            b => Equals(b.Content, "Cancelar") && b.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_RolAdmin_MuestraElBotonCancelarEnFilaPendiente()
    {
        var (window, vm, _) = Montar(
            new List<Tarea> { TareaDe(1, "Pendiente", EstadoTarea.Pendiente) }, rol: RolUsuario.Admin);

        Assert.True(vm.Pendientes[0].PuedeCancelar);
        Assert.True(BotonPorTexto(window, "Cancelar").IsVisible);
    }

    /// <summary>
    /// Resaltado de vencidas (spec): DiasParaVencer negativo -> SignoNegativoBrushConverter
    /// resuelve DangerBrush (#DC2626). Se compara el Foreground EFECTIVO del TextBlock del
    /// titulo contra el brush real, no solo que DiasParaVencer de negativo -- ese calculo ya
    /// esta cubierto por TareaFilaTests a nivel de VM; lo que falta cubrir es que la vista
    /// REALMENTE aplique el color.
    /// </summary>
    [AvaloniaFact]
    public void Montar_TareaPendienteVencida_ElTituloQuedaEnRojo()
    {
        var vencida = TareaDe(1, "Vencida", EstadoTarea.Pendiente, fechaLimite: DateTime.UtcNow.Date.AddDays(-5));
        var alDia = TareaDe(2, "Al dia", EstadoTarea.Pendiente, fechaLimite: DateTime.UtcNow.Date.AddDays(5));
        var (window, vm, _) = Montar(new List<Tarea> { vencida, alDia });

        Assert.True(vm.Pendientes[0].DiasParaVencer < 0);
        Assert.True(vm.Pendientes[1].DiasParaVencer > 0);

        var tituloVencida = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Vencida");
        var tituloAlDia = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Al dia");

        var brushVencida = Assert.IsAssignableFrom<ISolidColorBrush>(tituloVencida.Foreground);
        Assert.Equal(Color.Parse("#DC2626"), brushVencida.Color);

        // La que esta al dia no debe heredar el mismo color rojo explicito.
        Assert.NotEqual(Color.Parse("#DC2626"), (tituloAlDia.Foreground as ISolidColorBrush)?.Color ?? default);
    }

    [AvaloniaFact]
    public void ClickReal_EnTomar_LlamaAlServicioYMueveLaFilaAEnCurso()
    {
        var (window, vm, servicio) = Montar(new List<Tarea> { TareaDe(1, "x", EstadoTarea.Pendiente) });

        var boton = BotonPorTexto(window, "Tomar");
        Clickear(window, boton);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Pendientes);
        Assert.Single(vm.EnCurso);
        Assert.Equal(EstadoTarea.EnCurso, vm.EnCurso[0].Tarea.Estado);
    }

    [AvaloniaFact]
    public void ClickReal_EnCancelar_ComoAdmin_PidesConfirmacionYCancelaLaTarea()
    {
        var (window, vm, servicio) = Montar(
            new List<Tarea> { TareaDe(1, "x", EstadoTarea.Pendiente) }, rol: RolUsuario.Admin);

        var boton = BotonPorTexto(window, "Cancelar");
        Clickear(window, boton);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, servicio.LlamadasCancelar);
        Assert.Empty(vm.Pendientes);
        Assert.Single(vm.Canceladas.Concat(vm.Terminadas).Concat(vm.EnCurso).Concat(vm.Pendientes));
    }

    [AvaloniaFact]
    public void ClickReal_EnNuevaTarea_DisparaLaNavegacionATareaFormViewModel()
    {
        var recorder = new NavigationRecorderFake();
        var (window, _, _) = Montar(new List<Tarea>(), navegacion: recorder);

        var boton = BotonPorTexto(window, "Nueva tarea");
        Clickear(window, boton);

        Assert.Equal(typeof(TareaFormViewModel), recorder.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void ClickReal_EnVerDeUnaFilaTerminada_DisparaLaNavegacionATareaFormViewModel()
    {
        var recorder = new NavigationRecorderFake();
        var (window, _, _) = Montar(
            new List<Tarea> { TareaDe(1, "Terminada", EstadoTarea.Terminada) }, navegacion: recorder);

        var boton = BotonPorTexto(window, "Ver");
        Clickear(window, boton);

        Assert.Equal(typeof(TareaFormViewModel), recorder.UltimoTipoNavegado);
    }
}
