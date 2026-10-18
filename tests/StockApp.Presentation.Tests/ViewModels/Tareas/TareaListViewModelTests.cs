using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

public class TareaListViewModelTests
{
    private static Tarea TareaDe(int id, EstadoTarea estado, int? tomadaPorId = null) => new()
    {
        Id = id, Titulo = $"Tarea {id}", Estado = estado, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, TomadaPorUsuarioId = tomadaPorId,
    };

    private static (TareaListViewModel Vm, Mock<ITareaService> Svc, Mock<IConfirmacionService> Confirm)
        Crear(IReadOnlyList<Tarea>? tareas = null, RolUsuario rol = RolUsuario.Operador, HashSet<string>? permisos = null)
    {
        var svc = new Mock<ITareaService>();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(tareas ?? new List<Tarea>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.PermisosActuales).Returns(permisos ?? new HashSet<string>());

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new TareaListViewModel(svc.Object, session.Object, nav.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    [Fact]
    public async Task CargarAsync_ConRolOperador_FilaNoPuedeCancelar()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) }, rol: RolUsuario.Operador);

        await ctx.Vm.CargarAsync();

        Assert.False(ctx.Vm.Tareas[0].PuedeCancelar);
        Assert.True(ctx.Vm.Tareas[0].PuedeTomar);
    }

    [Fact]
    public async Task CargarAsync_ConRolAdmin_FilaPuedeCancelar()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) }, rol: RolUsuario.Admin);

        await ctx.Vm.CargarAsync();

        Assert.True(ctx.Vm.Tareas[0].PuedeCancelar);
    }

    [Fact]
    public async Task TomarAsync_LlamaAlServicioYRecarga()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) });
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Tareas[0];

        await ctx.Vm.TomarCommand.ExecuteAsync(fila);

        ctx.Svc.Verify(s => s.TomarAsync(1), Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_SinConfirmar_NoLlamaAlServicio()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) }, rol: RolUsuario.Admin);
        ctx.Confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Tareas[0];

        await ctx.Vm.CancelarCommand.ExecuteAsync(fila);

        ctx.Svc.Verify(s => s.CancelarAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task TomarAsync_ServidorNoDisponible_InformaAlUsuarioSinDejarLaTaskSinObservar()
    {
        // Mismo bug real que en IngresoPorFacturaViewModel (Fix 5): un AsyncRelayCommand que no
        // captura ServidorNoDisponibleException termina en crash.log sin avisar al operario. La carga ya no
        // informa por modal (queda SinConexion en línea); las acciones sobre la fila sí.
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) });
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Tareas[0];
        ctx.Svc.Setup(s => s.TomarAsync(1)).ThrowsAsync(new ServidorNoDisponibleException());

        await ctx.Vm.TomarCommand.ExecuteAsync(fila);

        ctx.Confirm.Verify(c => c.InformarAsync(ServidorNoDisponibleException.MensajePorDefecto), Times.Once);
    }

    [Fact]
    public async Task TomarAsync_SesionSinPermiso_InformaAlUsuario()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) });
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Tareas[0];
        ctx.Svc.Setup(s => s.TomarAsync(1)).ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.TomarCommand.ExecuteAsync(fila);

        ctx.Confirm.Verify(c => c.InformarAsync(TareaListViewModel.MensajeSinPermisoAccion), Times.Once);
    }

    // ── Carga protegida, filtro de estado en el cliente y revocación en caliente ──

    private static List<Tarea> LasCuatro() => new()
    {
        TareaDe(1, EstadoTarea.Pendiente),
        TareaDe(2, EstadoTarea.EnCurso),
        TareaDe(3, EstadoTarea.Terminada),
        TareaDe(4, EstadoTarea.Cancelada),
    };

    private static List<TareaFila> Visibles(TareaListViewModel vm) => vm.TareasView.Cast<TareaFila>().ToList();

    [Fact]
    public async Task CargarAsync_LlenaTareas()
    {
        var ctx = Crear(LasCuatro());

        await ctx.Vm.CargarAsync();

        Assert.Equal(4, ctx.Vm.Tareas.Count);
    }

    [Fact]
    public async Task FiltroPorDefecto_Abiertas_MuestraPendientesYEnCurso()
    {
        var ctx = Crear(LasCuatro());

        await ctx.Vm.CargarAsync();

        Assert.Equal("Abiertas", ctx.Vm.FiltroEstado.Nombre);
        Assert.Equal(2, ctx.Vm.CantidadVisible);
        Assert.All(Visibles(ctx.Vm), f => Assert.True(f.Tarea.Estado is EstadoTarea.Pendiente or EstadoTarea.EnCurso));
        Assert.Equal(2, Visibles(ctx.Vm).Count);
    }

    [Fact]
    public void Filtros_EstanEnElOrdenDelCombo()
    {
        var ctx = Crear();

        Assert.Equal(
            new[] { "Abiertas", "Pendientes", "En curso", "Terminadas", "Canceladas", "Todas" },
            ctx.Vm.FiltrosEstado.Select(o => o.Nombre));
    }

    [Fact]
    public async Task FiltroCanceladas_MuestraSoloLasCanceladas()
    {
        var ctx = Crear(LasCuatro());
        await ctx.Vm.CargarAsync();

        ctx.Vm.FiltroEstado = ctx.Vm.FiltrosEstado.Single(o => o.Nombre == "Canceladas");

        var visibles = Visibles(ctx.Vm);
        Assert.Single(visibles);
        Assert.Equal(EstadoTarea.Cancelada, visibles[0].Tarea.Estado);
        Assert.Equal(1, ctx.Vm.CantidadVisible);
    }

    [Fact]
    public async Task FiltroTodas_MuestraLasCuatro()
    {
        var ctx = Crear(LasCuatro());
        await ctx.Vm.CargarAsync();

        ctx.Vm.FiltroEstado = ctx.Vm.FiltrosEstado.Single(o => o.Nombre == "Todas");

        Assert.Equal(4, Visibles(ctx.Vm).Count);
        Assert.Equal(4, ctx.Vm.CantidadVisible);
    }

    [Fact]
    public async Task FiltroQueOcultaLaFilaSeleccionada_LimpiaLaSeleccion()
    {
        var ctx = Crear(LasCuatro());
        await ctx.Vm.CargarAsync();
        ctx.Vm.FilaSeleccionada = ctx.Vm.Tareas.First(f => f.Tarea.Estado == EstadoTarea.EnCurso);

        ctx.Vm.FiltroEstado = ctx.Vm.FiltrosEstado.Single(o => o.Nombre == "Pendientes");

        Assert.Null(ctx.Vm.FilaSeleccionada);
    }

    [Fact]
    public async Task FiltroQueNoOcultaLaSeleccion_LaConserva()
    {
        var ctx = Crear(LasCuatro());
        await ctx.Vm.CargarAsync();
        var enCurso = ctx.Vm.Tareas.First(f => f.Tarea.Estado == EstadoTarea.EnCurso);
        ctx.Vm.FilaSeleccionada = enCurso;

        ctx.Vm.FiltroEstado = ctx.Vm.FiltrosEstado.Single(o => o.Nombre == "Todas");

        Assert.Same(enCurso, ctx.Vm.FilaSeleccionada);
    }

    [Fact]
    public async Task CargarAsync_ServidorCaido_SinConexionSinAvisoYVacia()
    {
        var ctx = Crear(LasCuatro());
        await ctx.Vm.CargarAsync();
        ctx.Svc.Setup(s => s.ListarAsync()).ThrowsAsync(new ServidorNoDisponibleException());

        await ctx.Vm.CargarAsync();

        Assert.True(ctx.Vm.SinConexion);
        Assert.Empty(ctx.Vm.Tareas);
        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CargarAsync_Un403_SinPermisoConElMensajeYSinAviso()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarAsync()).ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.CargarAsync();

        Assert.True(ctx.Vm.SinPermiso);
        Assert.Equal(TareaListViewModel.MensajeSinPermisoTareas, ctx.Vm.MensajeSinPermiso);
        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DosCargasEnOrdenInverso_LaViejaNoPisaLaNueva()
    {
        var ctx = Crear();
        var primera = new TaskCompletionSource<IReadOnlyList<Tarea>>();
        var segunda = new TaskCompletionSource<IReadOnlyList<Tarea>>();
        ctx.Svc.SetupSequence(s => s.ListarAsync()).Returns(primera.Task).Returns(segunda.Task);

        var cargaVieja = ctx.Vm.CargarAsync();
        var cargaNueva = ctx.Vm.CargarAsync();
        segunda.SetResult(new List<Tarea> { TareaDe(9, EstadoTarea.Pendiente) });
        await cargaNueva;
        primera.SetResult(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente), TareaDe(2, EstadoTarea.Pendiente), TareaDe(3, EstadoTarea.Pendiente) });
        await cargaVieja;

        Assert.Single(ctx.Vm.Tareas);
        Assert.Equal(9, ctx.Vm.Tareas[0].Id);
    }

    [Fact]
    public async Task ReintentarCargaCommand_TrasUnaCaida_VuelveAConsultar()
    {
        var ctx = Crear(LasCuatro());
        ctx.Svc.SetupSequence(s => s.ListarAsync())
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(LasCuatro());
        await ctx.Vm.CargarAsync();
        Assert.True(ctx.Vm.SinConexion);

        await ctx.Vm.ReintentarCargaCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ListarAsync(), Times.Exactly(2));
        Assert.False(ctx.Vm.SinConexion);
        Assert.Equal(4, ctx.Vm.Tareas.Count);
    }

    [Fact]
    public async Task NotificarPermisosActualizados_SinGestionarTareas_VaciaYMarcaSinPermiso()
    {
        var permisos = new HashSet<string> { Permisos.GestionarTareas };
        var ctx = Crear(LasCuatro(), permisos: permisos);
        await ctx.Vm.CargarAsync();
        Assert.Equal(4, ctx.Vm.Tareas.Count);

        permisos.Remove(Permisos.GestionarTareas);
        ctx.Vm.NotificarPermisosActualizados();

        Assert.Empty(ctx.Vm.Tareas);
        Assert.True(ctx.Vm.SinPermiso);
        Assert.Equal(TareaListViewModel.MensajeSinPermisoTareas, ctx.Vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task NotificarPermisosActualizados_ConGestionarTareas_NoTocaNada()
    {
        var permisos = new HashSet<string> { Permisos.GestionarTareas };
        var ctx = Crear(LasCuatro(), permisos: permisos);
        await ctx.Vm.CargarAsync();

        ctx.Vm.NotificarPermisosActualizados();

        Assert.Equal(4, ctx.Vm.Tareas.Count);
        Assert.False(ctx.Vm.SinPermiso);
    }
}
