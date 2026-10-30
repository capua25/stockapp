using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Proyectos;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Proyectos;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Alta y ficha de Proyecto v2 con la View REAL: campos, solo lectura de un proyecto cerrado, botones de estado
/// según las transiciones del dominio (clicks reales), gate de gestión con un Operador mixto y "Nueva tarea".</summary>
public class ProyectoFormViewV2Tests
{
    private sealed record Montaje(Window Ventana, ProyectoFormView Vista, ProyectoFormViewModel Vm, ProyectoServiceFake Servicio,
        NavigationRecorderFake Navegacion);

    private static Montaje Montar(Action<ProyectoFormViewModel> cargar, ProyectoServiceFake servicio, ICurrentSession? sesion = null,
        TareaServiceFake? tareas = null)
    {
        var navegacion = new NavigationRecorderFake();
        var vm = new ProyectoFormViewModel(servicio, tareas ?? new TareaServiceFake(),
            new ZonaServiceFake(new List<Zona> { new() { Id = 1, Nombre = "Centro", Activo = true } }),
            new DimensionTematicaServiceFake(), new OrganismoResponsableServiceFake(), new OrigenFinanciamientoServiceFake(),
            sesion ?? new SesionFake(RolUsuario.Admin), navegacion, new ConfirmacionServiceFake());
        cargar(vm);
        var vista = new ProyectoFormView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, navegacion);
    }

    private static ProyectoServiceFake Con(EstadoProyecto estado) => new(new List<Proyecto>
    {
        new() { Id = 5, Titulo = "Rambla", Estado = estado, Prioridad = PrioridadTarea.Alta, ZonaId = 1,
                Zona = new Zona { Id = 1, Nombre = "Centro", Activo = true } },
    });

    private static T Control<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool VisibleNombre(Montaje m, string nombre) => ArbolVisual.EsVisibleEnArbol(Control<Control>(m, nombre));

    private static readonly string[] BotonesDeEstado =
        ["BotonIniciar", "BotonVolverAPendiente", "BotonTerminar", "BotonReabrir", "BotonCancelar"];

    private static string[] EstadoVisibles(Montaje m) => BotonesDeEstado.Where(n => VisibleNombre(m, n)).ToArray();

    [AvaloniaFact]
    public void Alta_CamposEditables_SinSeccionesDeFicha_GuardarEsElUnicoPrimario()
    {
        var m = Montar(vm => vm.CargarParaCrear(), new ProyectoServiceFake());

        Assert.True(VisibleNombre(m, "EncabezadoAlta"));
        Assert.False(VisibleNombre(m, "SeccionFicha"));
        Assert.True(Control<TextBox>(m, "CajaTitulo").IsEnabled);
        Assert.True(Control<ComboBox>(m, "ComboZona").IsEnabled);
        Assert.Equal(["(ninguna)", "Centro"], m.Vm.ZonasDisponibles.Select(o => o.Nombre).ToArray());
        var primarios = m.Vista.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario") && ArbolVisual.EsVisibleEnArbol(b));
        Assert.Same(Control<Button>(m, "BotonGuardar"), Assert.Single(primarios));
    }

    [AvaloniaFact]
    public void Alta_ClickGuardar_CreaYAbreLaFicha()
    {
        var m = Montar(vm => vm.CargarParaCrear(), new ProyectoServiceFake());
        m.Vm.Titulo = "Plaza nueva";
        m.Vm.PresupuestoTexto = "1.500";
        Dispatcher.UIThread.RunJobs();

        MontajeV2.Clic(m.Ventana, Control<Button>(m, "BotonGuardar"));

        var creado = Assert.Single(m.Servicio.Creados);
        Assert.Equal("Plaza nueva", creado.Titulo);
        Assert.Equal(1500m, creado.Presupuesto);
        Assert.Equal(typeof(ProyectoFormViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaTheory]
    [InlineData(EstadoProyecto.Pendiente, new[] { "BotonIniciar", "BotonCancelar" })]
    [InlineData(EstadoProyecto.EnCurso, new[] { "BotonVolverAPendiente", "BotonTerminar", "BotonCancelar" })]
    [InlineData(EstadoProyecto.Terminado, new[] { "BotonReabrir" })]
    [InlineData(EstadoProyecto.Cancelado, new[] { "BotonReabrir" })]
    public void Ficha_BotonesDeEstado_SegunElEstado(EstadoProyecto estado, string[] esperados)
    {
        var m = Montar(vm => vm.CargarParaVer(5), Con(estado));

        Assert.Equal(esperados, EstadoVisibles(m));
    }

    [AvaloniaFact]
    public void Ficha_TextosDeLosBotonesDeEstado()
    {
        var m = Montar(vm => vm.CargarParaVer(5), Con(EstadoProyecto.EnCurso));

        Assert.Equal(["Iniciar", "Volver a pendiente", "Terminar", "Reabrir", "Cancelar"],
            BotonesDeEstado.Select(n => (string)Control<Button>(m, n).Content!).ToArray());
    }

    [AvaloniaFact]
    public void Ficha_Cerrado_CamposEnSoloLecturaSinGuardar_ClickReabrirLosHabilita()
    {
        var m = Montar(vm => vm.CargarParaVer(5), Con(EstadoProyecto.Terminado));

        Assert.False(Control<TextBox>(m, "CajaTitulo").IsEnabled);
        Assert.False(Control<ComboBox>(m, "ComboZona").IsEnabled);
        Assert.Equal("Centro", ((OpcionCatalogoProyecto)Control<ComboBox>(m, "ComboZona").SelectedItem!).Nombre);
        Assert.False(VisibleNombre(m, "BotonGuardar"));

        MontajeV2.Clic(m.Ventana, Control<Button>(m, "BotonReabrir"));

        Assert.Equal((5, EstadoProyecto.EnCurso), Assert.Single(m.Servicio.CambiosDeEstado));
        Assert.True(Control<TextBox>(m, "CajaTitulo").IsEnabled);
        Assert.True(VisibleNombre(m, "BotonGuardar"));
        Assert.Equal("En curso", Control<Insignia>(m, "InsigniaEstado").Texto);
    }

    [AvaloniaFact]
    public void Ficha_OperadorSinGestionar_SoloLecturaSinBotonesDeEstadoNiNotaNueva()
    {
        var m = Montar(vm => vm.CargarParaVer(5), Con(EstadoProyecto.EnCurso),
            new SesionFake(RolUsuario.Operador, Permisos.VerProyectos, Permisos.GestionarTareas));

        Assert.Empty(EstadoVisibles(m));
        Assert.False(Control<TextBox>(m, "CajaTitulo").IsEnabled);
        Assert.False(VisibleNombre(m, "BotonGuardar"));
        Assert.False(VisibleNombre(m, "PanelNuevaNota"));
    }

    [AvaloniaFact]
    public void Ficha_TareasDelProyecto_ClickNuevaTareaNavegaConElProyecto()
    {
        var tareas = new TareaServiceFake(new List<Tarea>
        {
            new() { Id = 1, Titulo = "Bacheo", Estado = EstadoTarea.Pendiente, ProyectoId = 5 },
            new() { Id = 2, Titulo = "Otra", Estado = EstadoTarea.Pendiente, ProyectoId = 9 },
        });
        var m = Montar(vm => vm.CargarParaVer(5), Con(EstadoProyecto.EnCurso), tareas: tareas);

        var grilla = Control<DataGrid>(m, "GrillaTareas");
        Assert.Equal("Bacheo", Assert.Single(grilla.ItemsSource!.Cast<TareaFila>()).Titulo);
        var boton = Control<Button>(m, "BotonNuevaTarea");
        boton.BringIntoView();   // la sección de tareas queda debajo del pliegue: el click real necesita verla
        Dispatcher.UIThread.RunJobs();
        MontajeV2.Clic(m.Ventana, boton);

        Assert.Equal(typeof(TareaFormViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.NotNull(m.Navegacion.UltimoInicializadorTareaForm);
    }

    [AvaloniaFact]
    public void Ficha_OperadorSinGestionarTareas_SinSeccionTareas()
    {
        var m = Montar(vm => vm.CargarParaVer(5), Con(EstadoProyecto.EnCurso),
            new SesionFake(RolUsuario.Operador, Permisos.VerProyectos, Permisos.GestionarProyectos));

        Assert.False(VisibleNombre(m, "SeccionTareas"));
        Assert.True(VisibleNombre(m, "SeccionNotas"));
    }
}
