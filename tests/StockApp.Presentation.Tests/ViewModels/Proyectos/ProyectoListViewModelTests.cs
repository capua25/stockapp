using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Proyectos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Proyectos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Proyectos;

public class ProyectoListViewModelTests
{
    private static Proyecto ProyectoDe(int id, EstadoProyecto estado, decimal? presupuesto = null, Zona? zona = null) => new()
    {
        Id = id, Titulo = $"Proyecto {id}", Estado = estado, Prioridad = PrioridadTarea.Alta,
        Presupuesto = presupuesto, Zona = zona, ZonaId = zona?.Id, FechaCreacion = DateTime.UtcNow,
    };

    private static (ProyectoListViewModel Vm, Mock<IProyectoService> Svc, Mock<INavigationService> Nav, Mock<ICurrentSession> Sesion)
        Crear(IReadOnlyList<Proyecto>? proyectos = null, RolUsuario rol = RolUsuario.Operador, params string[] permisos)
    {
        var svc = new Mock<IProyectoService>();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(proyectos ?? new List<Proyecto>());
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));
        var nav = new Mock<INavigationService>();
        return (new ProyectoListViewModel(svc.Object, sesion.Object, nav.Object), svc, nav, sesion);
    }

    [Fact]
    public async Task CargarAsync_FiltroAbiertosPorDefecto_SoloPendientesYEnCurso()
    {
        var ctx = Crear(new List<Proyecto>
        {
            ProyectoDe(1, EstadoProyecto.Pendiente), ProyectoDe(2, EstadoProyecto.EnCurso),
            ProyectoDe(3, EstadoProyecto.Terminado), ProyectoDe(4, EstadoProyecto.Cancelado),
        }, RolUsuario.Operador, Permisos.VerProyectos);

        await ctx.Vm.CargarAsync();

        Assert.Equal(4, ctx.Vm.Proyectos.Count);
        Assert.Equal(2, ctx.Vm.CantidadVisible);
        Assert.Equal(new[] { 1, 2 }, ctx.Vm.ProyectosView.Cast<ProyectoFila>().Select(f => f.Id).ToArray());
    }

    [Fact]
    public async Task FiltroTodos_MuestraLosCuatroEstados()
    {
        var ctx = Crear(new List<Proyecto>
        {
            ProyectoDe(1, EstadoProyecto.Pendiente), ProyectoDe(3, EstadoProyecto.Terminado), ProyectoDe(4, EstadoProyecto.Cancelado),
        }, RolUsuario.Admin);
        await ctx.Vm.CargarAsync();

        ctx.Vm.FiltroEstado = ctx.Vm.FiltrosEstado.Single(o => o.Nombre == "Todos");

        Assert.Equal(3, ctx.Vm.CantidadVisible);
    }

    [Fact]
    public void Fila_SinPresupuestoNiZona_MuestraGuiones()
    {
        var fila = new ProyectoFila(ProyectoDe(1, EstadoProyecto.Pendiente));

        Assert.Equal("—", fila.PresupuestoTexto);
        Assert.Equal("—", fila.ZonaNombre);
    }

    [Fact]
    public void Fila_ConPresupuestoYZona_FormateaEsUy()
    {
        var fila = new ProyectoFila(ProyectoDe(1, EstadoProyecto.Pendiente, 1234.5m, new Zona { Id = 2, Nombre = "Centro" }));

        Assert.Equal("$ 1.234,50", fila.PresupuestoTexto);
        Assert.Equal("Centro", fila.ZonaNombre);
    }

    [Theory]
    [InlineData(RolUsuario.Admin, new string[0], true)]
    [InlineData(RolUsuario.Operador, new[] { Permisos.VerProyectos, Permisos.GestionarProyectos }, true)]
    [InlineData(RolUsuario.Operador, new[] { Permisos.VerProyectos, Permisos.GestionarTareas }, false)]
    public void PuedeGestionarProyectos_SegunRolYPermiso(RolUsuario rol, string[] permisos, bool esperado)
    {
        var ctx = Crear(rol: rol, permisos: permisos);
        Assert.Equal(esperado, ctx.Vm.PuedeGestionarProyectos);
    }

    [Fact]
    public async Task VerDetalle_NavegaALaFichaDelProyecto()
    {
        var ctx = Crear(new List<Proyecto> { ProyectoDe(8, EstadoProyecto.EnCurso) }, RolUsuario.Admin);
        await ctx.Vm.CargarAsync();

        ctx.Vm.VerDetalleCommand.Execute(ctx.Vm.Proyectos[0]);

        ctx.Nav.Verify(n => n.Navegar(It.IsAny<Action<ProyectoFormViewModel>>()), Times.Once);
    }

    [Fact]
    public async Task CargarAsync_403_MarcaSinPermiso()
    {
        var ctx = Crear(rol: RolUsuario.Operador, permisos: Permisos.VerProyectos);
        ctx.Svc.Setup(s => s.ListarAsync()).ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.CargarAsync();

        Assert.True(ctx.Vm.SinPermiso);
        Assert.Equal(ProyectoListViewModel.MensajeSinPermisoProyectos, ctx.Vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task PermisoRevocadoEnCaliente_VaciaYMarcaSinPermiso()
    {
        var ctx = Crear(new List<Proyecto> { ProyectoDe(1, EstadoProyecto.Pendiente) }, RolUsuario.Operador, Permisos.VerProyectos);
        await ctx.Vm.CargarAsync();
        ctx.Sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());

        ctx.Vm.NotificarPermisosActualizados();

        Assert.Empty(ctx.Vm.Proyectos);
        Assert.True(ctx.Vm.SinPermiso);
    }
}
