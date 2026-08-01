using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
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
        Crear(IReadOnlyList<Tarea>? tareas = null, RolUsuario rol = RolUsuario.Operador)
    {
        var svc = new Mock<ITareaService>();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(tareas ?? new List<Tarea>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new TareaListViewModel(svc.Object, session.Object, nav.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    [Fact]
    public async Task CargarAsync_AgrupaTareasPorEstado()
    {
        var ctx = Crear(new List<Tarea>
        {
            TareaDe(1, EstadoTarea.Pendiente),
            TareaDe(2, EstadoTarea.EnCurso),
            TareaDe(3, EstadoTarea.Terminada),
            TareaDe(4, EstadoTarea.Cancelada),
        });

        await ctx.Vm.CargarAsync();

        Assert.Single(ctx.Vm.Pendientes);
        Assert.Single(ctx.Vm.EnCurso);
        Assert.Single(ctx.Vm.Terminadas);
        Assert.Single(ctx.Vm.Canceladas);
        Assert.Equal(1, ctx.Vm.Pendientes[0].Id);
        Assert.Equal(4, ctx.Vm.Canceladas[0].Id);
    }

    [Fact]
    public async Task CargarAsync_ConRolOperador_FilaNoPuedeCancelar()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) }, rol: RolUsuario.Operador);

        await ctx.Vm.CargarAsync();

        Assert.False(ctx.Vm.Pendientes[0].PuedeCancelar);
        Assert.True(ctx.Vm.Pendientes[0].PuedeTomar);
    }

    [Fact]
    public async Task CargarAsync_ConRolAdmin_FilaPuedeCancelar()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) }, rol: RolUsuario.Admin);

        await ctx.Vm.CargarAsync();

        Assert.True(ctx.Vm.Pendientes[0].PuedeCancelar);
    }

    [Fact]
    public async Task TomarAsync_LlamaAlServicioYRecarga()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) });
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Pendientes[0];

        await ctx.Vm.TomarCommand.ExecuteAsync(fila);

        ctx.Svc.Verify(s => s.TomarAsync(1), Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_SinConfirmar_NoLlamaAlServicio()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) }, rol: RolUsuario.Admin);
        ctx.Confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Pendientes[0];

        await ctx.Vm.CancelarCommand.ExecuteAsync(fila);

        ctx.Svc.Verify(s => s.CancelarAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CargarAsync_ServidorNoDisponible_InformaAlUsuarioSinDejarLaTaskSinObservar()
    {
        // Mismo bug real que en IngresoPorFacturaViewModel (Fix 5): un AsyncRelayCommand que no
        // captura ServidorNoDisponibleException termina en crash.log sin avisar al operario.
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarAsync()).ThrowsAsync(new ServidorNoDisponibleException());

        await ctx.Vm.CargarAsync();

        ctx.Confirm.Verify(c => c.InformarAsync(ServidorNoDisponibleException.MensajePorDefecto), Times.Once);
    }

    [Fact]
    public async Task TomarAsync_SesionSinPermiso_InformaAlUsuario()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) });
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Pendientes[0];
        ctx.Svc.Setup(s => s.TomarAsync(1)).ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.TomarCommand.ExecuteAsync(fila);

        ctx.Confirm.Verify(c => c.InformarAsync(TareaListViewModel.MensajeSinPermiso), Times.Once);
    }
}
