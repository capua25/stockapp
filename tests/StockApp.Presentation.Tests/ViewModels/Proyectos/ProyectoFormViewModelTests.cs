using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Application.Proyectos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Proyectos;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Proyectos;

public class ProyectoFormViewModelTests
{
    private sealed record Contexto(
        ProyectoFormViewModel Vm, Mock<IProyectoService> Proyectos, Mock<ITareaService> Tareas,
        Mock<INavigationService> Nav, Mock<IConfirmacionService> Confirm, Mock<IZonaService> Zonas);

    private static Proyecto ProyectoDe(int id, EstadoProyecto estado, decimal? presupuesto = null) => new()
    {
        Id = id, Titulo = $"Proyecto {id}", Estado = estado, Prioridad = PrioridadTarea.Media,
        Presupuesto = presupuesto, FechaCreacion = DateTime.UtcNow,
    };

    private static Contexto Crear(RolUsuario rol = RolUsuario.Admin, params string[] permisos)
    {
        var proyectos = new Mock<IProyectoService>();
        var tareas = new Mock<ITareaService>();
        tareas.Setup(t => t.ListarPorProyectoAsync(It.IsAny<int>())).ReturnsAsync(new List<Tarea>());
        var zonas = new Mock<IZonaService>();
        zonas.Setup(z => z.ListarActivasAsync()).ReturnsAsync(new List<Zona>());
        var dimensiones = new Mock<IDimensionTematicaService>();
        dimensiones.Setup(d => d.ListarActivasAsync()).ReturnsAsync(new List<DimensionTematica>());
        var organismos = new Mock<IOrganismoResponsableService>();
        organismos.Setup(o => o.ListarActivasAsync()).ReturnsAsync(new List<OrganismoResponsable>());
        var origenes = new Mock<IOrigenFinanciamientoService>();
        origenes.Setup(o => o.ListarActivasAsync()).ReturnsAsync(new List<OrigenFinanciamiento>());

        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new ProyectoFormViewModel(
            proyectos.Object, tareas.Object, zonas.Object, dimensiones.Object, organismos.Object, origenes.Object,
            sesion.Object, nav.Object, confirm.Object);
        return new Contexto(vm, proyectos, tareas, nav, confirm, zonas);
    }

    private static async Task<Contexto> AbrirFicha(FichaProyectoDto ficha, RolUsuario rol = RolUsuario.Admin, params string[] permisos)
    {
        var ctx = Crear(rol, permisos);
        ctx.Proyectos.Setup(p => p.ObtenerFichaAsync(ficha.Proyecto.Id)).ReturnsAsync(ficha);
        ctx.Vm.CargarParaVer(ficha.Proyecto.Id);
        await ctx.Vm.InicializarAsync();
        return ctx;
    }

    [Fact]
    public async Task CargarParaVer_SinPresupuesto_MuestraSoloTotalGastado()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso), 1500m, null, new List<GastoDeProyectoDto>()));

        Assert.Equal(1500m, ctx.Vm.TotalGastado);
        Assert.False(ctx.Vm.TienePresupuesto);
        Assert.False(ctx.Vm.MuestraDisponible);
        Assert.False(ctx.Vm.HaySobregiro);
    }

    [Fact]
    public async Task CargarParaVer_PresupuestoCero_MuestraSobregiro()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso, 0m), 1500m, -1500m, new List<GastoDeProyectoDto>()));

        Assert.True(ctx.Vm.TienePresupuesto);
        Assert.Equal(0m, ctx.Vm.Presupuesto);
        Assert.True(ctx.Vm.HaySobregiro);
        Assert.Equal(1500m, ctx.Vm.Sobregiro);
        Assert.False(ctx.Vm.MuestraDisponible);
    }

    [Fact]
    public async Task CargarParaVer_ConPresupuestoSinGastarlo_MuestraDisponibleSinSobregiro()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso, 5000m), 1500m, 3500m, new List<GastoDeProyectoDto>()));

        Assert.True(ctx.Vm.MuestraDisponible);
        Assert.Equal(3500m, ctx.Vm.Disponible);
        Assert.False(ctx.Vm.HaySobregiro);
    }

    [Fact]
    public async Task CargarParaVer_GastosNull_OcultaListaDeGastos()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso, 1000m), 400m, 600m, null),
            RolUsuario.Operador, Permisos.VerProyectos);

        Assert.False(ctx.Vm.MuestraListaGastos);
        Assert.Empty(ctx.Vm.Gastos);
        Assert.Equal(400m, ctx.Vm.TotalGastado);
    }

    [Fact]
    public async Task CargarParaVer_ConGastos_MuestraLaListaDeGastos()
    {
        var gastos = new List<GastoDeProyectoDto> { new(9, new DateTime(2026, 10, 1), "Barraca Sur", "Arena", 400m) };
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso), 400m, null, gastos));

        Assert.True(ctx.Vm.MuestraListaGastos);
        Assert.Equal("Barraca Sur", Assert.Single(ctx.Vm.Gastos).ProveedorNombre);
    }

    [Theory]
    [InlineData(EstadoProyecto.Pendiente, false)]
    [InlineData(EstadoProyecto.EnCurso, false)]
    [InlineData(EstadoProyecto.Terminado, true)]
    [InlineData(EstadoProyecto.Cancelado, true)]
    public async Task BotonReabrir_SoloEnCerrado(EstadoProyecto estado, bool esperado)
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, estado), 0m, null, null));

        Assert.Equal(esperado, ctx.Vm.PuedeReabrir);
        // "Iniciar" también va a En curso, pero solo desde Pendiente: nunca se ofrecen los dos a la vez.
        Assert.False(ctx.Vm.PuedeReabrir && ctx.Vm.PuedeIniciar);
    }

    [Fact]
    public async Task BotonesDeEstado_SiguenLasTransicionesDelDominio()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso), 0m, null, null));

        Assert.False(ctx.Vm.PuedeIniciar);
        Assert.True(ctx.Vm.PuedeVolverAPendiente);
        Assert.True(ctx.Vm.PuedeTerminar);
        Assert.True(ctx.Vm.PuedeCancelar);
        Assert.False(ctx.Vm.PuedeReabrir);
    }

    [Fact]
    public async Task BotonesDeEstado_SinPermisoDeGestion_NoSeMuestran()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.Terminado), 0m, null, null),
            RolUsuario.Operador, Permisos.VerProyectos);

        Assert.False(ctx.Vm.PuedeReabrir);
        Assert.False(ctx.Vm.EsEditable);
    }

    [Fact]
    public async Task Reabrir_CambiaElEstadoAEnCursoYRecargaLaFicha()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.Terminado), 0m, null, null));
        ctx.Proyectos.Setup(p => p.ObtenerFichaAsync(3))
            .ReturnsAsync(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso), 0m, null, null));

        await ctx.Vm.ReabrirCommand.ExecuteAsync(null);

        ctx.Proyectos.Verify(p => p.CambiarEstadoAsync(3, EstadoProyecto.EnCurso), Times.Once);
        Assert.True(ctx.Vm.EsEditable);
        Assert.False(ctx.Vm.PuedeReabrir);
    }

    [Theory]
    [InlineData(EstadoProyecto.Terminado)]
    [InlineData(EstadoProyecto.Cancelado)]
    public async Task Guardar_ProyectoCerrado_SoloLectura(EstadoProyecto estado)
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, estado), 0m, null, null));
        ctx.Vm.Titulo = "Otro título";

        Assert.False(ctx.Vm.EsEditable);
        Assert.False(ctx.Vm.GuardarCommand.CanExecute(null));
        ctx.Proyectos.Verify(p => p.ModificarAsync(It.IsAny<int>(), It.IsAny<DatosProyecto>()), Times.Never);
    }

    [Fact]
    public async Task Guardar_ProyectoAbierto_ModificaConLosDatosDelFormulario()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso, 1000m), 0m, 1000m, null));
        ctx.Vm.Titulo = "Rambla";
        ctx.Vm.PresupuestoTexto = "2.500,50";

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        ctx.Proyectos.Verify(p => p.ModificarAsync(3, It.Is<DatosProyecto>(d =>
            d.Titulo == "Rambla" && d.Presupuesto == 2500.50m && d.Prioridad == PrioridadTarea.Media)), Times.Once);
    }

    [Fact]
    public async Task Guardar_RecargaBien_InformaProyectoGuardado()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso), 0m, null, null));
        ctx.Vm.Titulo = "Rambla";

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        ctx.Confirm.Verify(c => c.InformarAsync("Proyecto guardado."), Times.Once);
    }

    [Fact]
    public async Task Guardar_RecargaSinServidor_NoInformaProyectoGuardado()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso), 0m, null, null));
        ctx.Vm.Titulo = "Rambla";
        ctx.Proyectos.Setup(p => p.ObtenerFichaAsync(3)).ThrowsAsync(new StockApp.ApiClient.ServidorNoDisponibleException());

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        ctx.Proyectos.Verify(p => p.ModificarAsync(3, It.IsAny<DatosProyecto>()), Times.Once);
        Assert.True(ctx.Vm.SinConexion);
        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Guardar_RecargaSinPermiso_NoInformaProyectoGuardado()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.EnCurso), 0m, null, null));
        ctx.Vm.Titulo = "Rambla";
        ctx.Proyectos.Setup(p => p.ObtenerFichaAsync(3)).ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(ctx.Vm.SinPermiso);
        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Inicializar_UnCatalogoSinPermiso_MarcaSinPermisoComoGastoForm()
    {
        var ctx = Crear();
        ctx.Vm.CargarParaCrear();
        ctx.Zonas.Setup(z => z.ListarActivasAsync()).ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.InicializarAsync();

        Assert.True(ctx.Vm.SinPermiso);
    }

    [Fact]
    public async Task Guardar_Alta_CreaElProyectoConPresupuestoVacioComoNull()
    {
        var ctx = Crear();
        ctx.Proyectos.Setup(p => p.CrearAsync(It.IsAny<DatosProyecto>())).ReturnsAsync(11);
        ctx.Vm.CargarParaCrear();
        await ctx.Vm.InicializarAsync();
        ctx.Vm.Titulo = "Rambla";

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        ctx.Proyectos.Verify(p => p.CrearAsync(It.Is<DatosProyecto>(d => d.Titulo == "Rambla" && d.Presupuesto == null)), Times.Once);
    }

    [Fact]
    public void Guardar_PresupuestoInvalido_NoSePuedeEjecutar()
    {
        var ctx = Crear();
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Titulo = "Rambla";
        ctx.Vm.PresupuestoTexto = "1.5";

        Assert.False(ctx.Vm.GuardarCommand.CanExecute(null));
        Assert.NotNull(ctx.Vm.ErrorDe(nameof(ProyectoFormViewModel.PresupuestoTexto)));
    }

    [Fact]
    public async Task NuevaTarea_NavegaConProyectoPreseleccionado()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(7, EstadoProyecto.EnCurso), 0m, null, null),
            RolUsuario.Operador, Permisos.VerProyectos, Permisos.GestionarTareas);
        Action<TareaFormViewModel>? inicializar = null;
        ctx.Nav.Setup(n => n.Navegar(It.IsAny<Action<TareaFormViewModel>>()))
            .Callback<Action<TareaFormViewModel>>(a => inicializar = a);

        ctx.Vm.NuevaTareaCommand.Execute(null);

        Assert.NotNull(inicializar);
        var opciones = new Mock<IProyectoService>();
        opciones.Setup(o => o.ListarOpcionesAsync()).ReturnsAsync(new List<OpcionProyectoDto>
        {
            new(5, "Arbolado", EstadoProyecto.EnCurso), new(7, "Proyecto 7", EstadoProyecto.EnCurso),
        });
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        var panel = new ClasificacionTareaPanelViewModel(opciones.Object, Mock.Of<IDocumentoAdministrativoService>(), Mock.Of<IConfirmacionService>());
        var formulario = new TareaFormViewModel(Mock.Of<ITareaService>(), sesion.Object, Mock.Of<INavigationService>(),
            Mock.Of<IConfirmacionService>(), panel, Mock.Of<IClasificacionTareaDialogService>());

        inicializar!(formulario);
        await formulario.InicializarAltaAsync();

        Assert.True(formulario.EsNuevaTarea);
        Assert.Equal(7, panel.ProyectoSeleccionado?.Id);
        Assert.Equal(7, panel.ObtenerDatos().ProyectoId);
    }

    [Fact]
    public async Task NuevaTarea_ProyectoCerradoOSinGestionarTareas_NoSeOfrece()
    {
        var cerrado = await AbrirFicha(new FichaProyectoDto(ProyectoDe(7, EstadoProyecto.Terminado), 0m, null, null));
        var sinTareas = await AbrirFicha(new FichaProyectoDto(ProyectoDe(7, EstadoProyecto.EnCurso), 0m, null, null),
            RolUsuario.Operador, Permisos.VerProyectos, Permisos.GestionarProyectos);

        Assert.False(cerrado.Vm.PuedeCrearTarea);
        Assert.False(sinTareas.Vm.PuedeCrearTarea);
        Assert.False(sinTareas.Vm.MuestraTareas);
        sinTareas.Tareas.Verify(t => t.ListarPorProyectoAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CargarParaVer_ListaLasTareasDelProyecto()
    {
        var ctx = Crear();
        ctx.Proyectos.Setup(p => p.ObtenerFichaAsync(7))
            .ReturnsAsync(new FichaProyectoDto(ProyectoDe(7, EstadoProyecto.EnCurso), 0m, null, null));
        ctx.Tareas.Setup(t => t.ListarPorProyectoAsync(7)).ReturnsAsync(new List<Tarea>
        {
            new() { Id = 1, Titulo = "Bacheo", Estado = EstadoTarea.Pendiente, ProyectoId = 7 },
        });
        ctx.Vm.CargarParaVer(7);

        await ctx.Vm.InicializarAsync();

        Assert.Equal("Bacheo", Assert.Single(ctx.Vm.Tareas).Titulo);
    }

    [Fact]
    public async Task AgregarNota_ProyectoCerrado_SigueDisponible()
    {
        var ctx = await AbrirFicha(new FichaProyectoDto(ProyectoDe(3, EstadoProyecto.Terminado), 0m, null, null));
        ctx.Vm.NuevaNotaTexto = "Quedó pendiente la señalización.";

        await ctx.Vm.AgregarNotaCommand.ExecuteAsync(null);

        ctx.Proyectos.Verify(p => p.AgregarNotaAsync(3, "Quedó pendiente la señalización."), Times.Once);
        Assert.Single(ctx.Vm.Notas);
        Assert.Equal(string.Empty, ctx.Vm.NuevaNotaTexto);
    }
}
