using System;
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

public class TareaFormViewModelTests
{
    private static (TareaFormViewModel Vm, Mock<ITareaService> Svc, Mock<IConfirmacionService> Confirm)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var svc = new Mock<ITareaService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new TareaFormViewModel(svc.Object, session.Object, nav.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    [Fact]
    public void CargarParaCrear_DejaLosCamposVaciosYModoAlta()
    {
        var ctx = Crear();
        ctx.Vm.CargarParaCrear();

        Assert.True(ctx.Vm.EsNuevaTarea);
        Assert.Equal(string.Empty, ctx.Vm.Titulo);
        Assert.Empty(ctx.Vm.Notas);
    }

    [Fact]
    public void CargarParaVer_PopulaCamposDeSoloLecturaYElHiloDeNotas()
    {
        var ctx = Crear();
        var tarea = new Tarea
        {
            Id = 5, Titulo = "Reparar bache", Descripcion = "En calle Rivera",
            Estado = EstadoTarea.EnCurso, TomadaPor = new Usuario { NombreUsuario = "juan" },
        };
        tarea.Notas.Add(new NotaTarea { Texto = "primera nota", Fecha = DateTime.UtcNow });

        ctx.Vm.CargarParaVer(tarea);

        Assert.False(ctx.Vm.EsNuevaTarea);
        Assert.Equal("Reparar bache", ctx.Vm.Titulo);
        Assert.Single(ctx.Vm.Notas);
        Assert.Equal("juan", ctx.Vm.TomadaPorNombre);
    }

    [Fact]
    public void GuardarCommand_TituloVacio_NoSePuedeEjecutar()
    {
        var ctx = Crear();
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Titulo = "   ";

        Assert.False(ctx.Vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarAsync_ConTitulo_CreaLaTareaYVuelveAlListado()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.CrearAsync(It.IsAny<Tarea>())).ReturnsAsync(9);
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Titulo = "Reparar bache";

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.CrearAsync(It.Is<Tarea>(t => t.Titulo == "Reparar bache")), Times.Once);
    }

    [Fact]
    public async Task AgregarNotaAsync_SumaLaNotaAlHiloSinRecargarTodo()
    {
        var ctx = Crear();
        var tarea = new Tarea { Id = 5, Titulo = "Reparar bache" };
        ctx.Vm.CargarParaVer(tarea);
        ctx.Vm.NuevaNotaTexto = "avance del día";

        await ctx.Vm.AgregarNotaCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AgregarNotaAsync(5, "avance del día"), Times.Once);
        Assert.Single(ctx.Vm.Notas);
        Assert.Equal("avance del día", ctx.Vm.Notas[0].Texto);
        Assert.Equal(string.Empty, ctx.Vm.NuevaNotaTexto);
        // La nota se suma localmente al hilo: no hace falta releer toda la tarea.
        ctx.Svc.Verify(s => s.ListarAsync(), Times.Never);
    }

    [Fact]
    public async Task CambiarPrioridadAsync_ComoAdmin_LlamaAlServicio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media };
        ctx.Vm.CargarParaVer(tarea);
        ctx.Vm.PrioridadSeleccionada = PrioridadTarea.Alta;

        await ctx.Vm.CambiarPrioridadCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.CambiarPrioridadAsync(5, PrioridadTarea.Alta), Times.Once);
    }

    [Fact]
    public async Task GuardarAsync_ServidorNoDisponible_MuestraMensajeDeErrorAccionable()
    {
        // Mismo fix que en IngresoPorFacturaViewModel: GuardarAsync es un AsyncRelayCommand;
        // si no captura ServidorNoDisponibleException, la excepción no llega al handler global
        // y termina en crash.log sin avisar al operario.
        var ctx = Crear();
        ctx.Svc.Setup(s => s.CrearAsync(It.IsAny<Tarea>())).ThrowsAsync(new ServidorNoDisponibleException());
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Titulo = "Reparar bache";

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, ctx.Vm.MensajeError);
    }

    [Fact]
    public async Task AgregarNotaAsync_SesionSinPermiso_MuestraMensajeDeErrorYNoAgregaLaNota()
    {
        var ctx = Crear();
        var tarea = new Tarea { Id = 5, Titulo = "Reparar bache" };
        ctx.Vm.CargarParaVer(tarea);
        ctx.Vm.NuevaNotaTexto = "avance del día";
        ctx.Svc.Setup(s => s.AgregarNotaAsync(5, "avance del día")).ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.AgregarNotaCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(ctx.Vm.MensajeError));
        Assert.Empty(ctx.Vm.Notas);
    }
}
