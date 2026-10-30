using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Proyectos;
using StockApp.Presentation.Views.Proyectos;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Listado de Proyectos v2 con la View REAL: estructura, columnas, filtro de Estado, gate de "Nuevo proyecto" con
/// un Operador de permisos mixtos, estados de pantalla y doble click.</summary>
public class ProyectoListViewV2Tests
{
    private static Proyecto P(int id, string titulo, EstadoProyecto estado, decimal? presupuesto = null, string? zona = null) => new()
    {
        Id = id, Titulo = titulo, Estado = estado, Prioridad = PrioridadTarea.Media, Presupuesto = presupuesto,
        FechaLimite = new DateTime(2030, 3, id, 0, 0, 0, DateTimeKind.Utc),
        Zona = zona is null ? null : new Zona { Id = 1, Nombre = zona, Activo = true }, ZonaId = zona is null ? null : 1,
    };

    private static List<Proyecto> Datos() =>
    [
        P(1, "Alumbrado de la rambla", EstadoProyecto.Pendiente, 250000m, "Centro"),
        P(2, "Arbolado urbano", EstadoProyecto.EnCurso),
        P(3, "Plaza de deportes", EstadoProyecto.Terminado, 90000m),
        P(4, "Bancos en la peatonal", EstadoProyecto.Cancelado),
    ];

    private sealed record Montaje(Window Ventana, ProyectoListView Vista, ProyectoListViewModel Vm, NavigationRecorderFake Navegacion);

    private static Montaje Montar(ProyectoServiceFake? servicio = null, ICurrentSession? sesion = null)
    {
        var navegacion = new NavigationRecorderFake();
        var vm = new ProyectoListViewModel(servicio ?? new ProyectoServiceFake(Datos()), sesion ?? new SesionFake(RolUsuario.Admin), navegacion);
        var vista = new ProyectoListView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, navegacion);
    }

    private static T Control<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);
    private static bool VisibleNombre(Montaje m, string nombre) => Visible(Control<Control>(m, nombre));

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoProyectos_NuevoProyectoEsElUnicoPrimario()
    {
        var m = Montar();
        var encabezado = Control<EncabezadoPagina>(m, "Encabezado");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Proyectos", encabezado.Titulo);
        var primarios = m.Vista.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario") && Visible(b));
        Assert.Same(Control<Button>(m, "BotonNuevo"), Assert.Single(primarios));
        Assert.Equal("Nuevo proyecto", Control<Button>(m, "BotonNuevo").Content);
        Assert.Same(m.Vm.NuevoCommand, Control<Button>(m, "BotonNuevo").Command);
    }

    [AvaloniaFact]
    public void Grilla_SeisColumnasEnOrden()
    {
        var m = Montar();

        Assert.Equal(["TÍTULO", "ESTADO", "PRIORIDAD", "ZONA", "PRESUPUESTO", "FECHA LÍMITE"],
            Control<DataGrid>(m, "Grilla").Columns.Select(c => (string)c.Header).ToArray());
    }

    [AvaloniaFact]
    public void FiltroEstado_PorDefectoAbiertos_ConteoYFilas()
    {
        var m = Montar();

        Assert.Equal("Abiertos", ((OpcionEstadoProyecto)Control<ComboBox>(m, "ComboEstado").SelectedItem!).Nombre);
        var filas = Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridRow>().Where(r => Visible(r)).ToList();
        Assert.Equal(2, filas.Count);
        Assert.Equal("2 proyectos", Control<TextBlock>(m, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void NuevoProyecto_OperadorSinGestionar_NoSeMuestra()
    {
        // Mixto: proyectos.ver y los dos de Tareas, sin proyectos.gestionar.
        var m = Montar(sesion: new SesionFake(RolUsuario.Operador, Permisos.VerProyectos, Permisos.GestionarTareas, Permisos.AdministrarTareas));

        Assert.False(VisibleNombre(m, "BotonNuevo"));
        Assert.True(VisibleNombre(m, "Grilla"));
    }

    [AvaloniaFact]
    public void NuevoProyecto_OperadorConGestionar_SeMuestraYNavegaAlAlta()
    {
        var m = Montar(sesion: new SesionFake(RolUsuario.Operador, Permisos.VerProyectos, Permisos.GestionarProyectos, Permisos.VerFinanzas));

        Assert.True(VisibleNombre(m, "BotonNuevo"));
        MontajeV2.Clic(m.Ventana, Control<Button>(m, "BotonNuevo"));

        Assert.Equal(typeof(ProyectoFormViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Ver_ConFilaSeleccionada_NavegaALaFicha()
    {
        var m = Montar();
        Assert.False(Control<Button>(m, "BotonVer").IsEnabled);

        m.Vm.FilaSeleccionada = m.Vm.Proyectos.Single(f => f.Id == 2);
        Dispatcher.UIThread.RunJobs();
        MontajeV2.Clic(m.Ventana, Control<Button>(m, "BotonVer"));

        Assert.Equal(typeof(ProyectoFormViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void SinProyectos_EstadoVacio()
    {
        var m = Montar(new ProyectoServiceFake());

        Assert.True(VisibleNombre(m, "EstadoSinProyectos"));
        Assert.False(VisibleNombre(m, "Grilla"));
    }

    [AvaloniaFact]
    public void Servidor403_SinPermisoEnLugarDeLaCard()
    {
        var m = Montar(new ProyectoServiceFake { ResponderListar = () => Task.FromException<IReadOnlyList<Proyecto>>(new UnauthorizedAccessException()) });

        Assert.True(VisibleNombre(m, "EstadoSinPermiso"));
        Assert.False(VisibleNombre(m, "Tarjeta"));
    }

    [AvaloniaFact]
    public void SinServidor_AvisoEnLineaConReintentar()
    {
        var m = Montar(new ProyectoServiceFake { ResponderListar = () => Task.FromException<IReadOnlyList<Proyecto>>(new ServidorNoDisponibleException()) });

        Assert.True(VisibleNombre(m, "EstadoSinConexion"));
        Assert.True(VisibleNombre(m, "BotonReintentar"));
    }
}
