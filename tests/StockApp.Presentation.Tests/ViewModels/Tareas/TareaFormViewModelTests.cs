using System;
using System.Threading.Tasks;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
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
    private static (TareaFormViewModel Vm, Mock<ITareaService> Svc, Mock<IConfirmacionService> Confirm,
                     Mock<IClasificacionTareaDialogService> DialogoClasificacion)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var svc = new Mock<ITareaService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var dialogoClasificacion = new Mock<IClasificacionTareaDialogService>();

        var panel = new ClasificacionTareaPanelViewModel(
            Mock.Of<IZonaService>(), Mock.Of<IDimensionTematicaService>(),
            Mock.Of<IOrganismoResponsableService>(), Mock.Of<IOrigenFinanciamientoService>(),
            Mock.Of<IDocumentoAdministrativoService>(), confirm.Object);

        var vm = new TareaFormViewModel(svc.Object, session.Object, nav.Object, confirm.Object, panel, dialogoClasificacion.Object);
        return (vm, svc, confirm, dialogoClasificacion);
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

    /// <summary>Fix (revisión final, Important 1): antes anteponía Tipo -- con la etiqueta
    /// "Expediente:" del XAML delante, se leía "Expediente: Expediente 0099/0" (palabra
    /// duplicada + año en 0 porque TareaApiClient no lo traía). Con Anio viajando de verdad,
    /// el texto queda "0099/2026" sin repetir la palabra que ya aporta la etiqueta.</summary>
    [Fact]
    public void CargarParaVer_ConDocumentoAdministrativo_FormateaNumeroBarraAnioSinRepetirLaPalabra()
    {
        var ctx = Crear();
        var tarea = new Tarea
        {
            Id = 5, Titulo = "Reparar bache", Estado = EstadoTarea.Pendiente,
            DocumentoAdministrativo = new DocumentoAdministrativo
            {
                Id = 9, Numero = "0099", Anio = 2026, Tipo = TipoDocumento.Expediente,
            },
        };

        ctx.Vm.CargarParaVer(tarea);

        Assert.Equal("0099/2026", ctx.Vm.DocumentoAdministrativoTexto);
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
    public async Task GuardarAsync_ConFechaLimite_NormalizaAUtcAntesDeEnviarla()
    {
        // Fix (review final, Important): CalendarDatePicker puede entregar Kind=Local; el
        // converter del servidor solo normaliza Unspecified, así que Npgsql rechazaría el
        // insert en timestamptz. DateTime.== ignora Kind, por eso se assertea Kind
        // explícitamente -- de lo contrario este test "pasaría" incluso sin el fix.
        var ctx = Crear();
        Tarea? tareaCreada = null;
        ctx.Svc.Setup(s => s.CrearAsync(It.IsAny<Tarea>()))
            .Callback<Tarea>(t => tareaCreada = t)
            .ReturnsAsync(1);
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Titulo = "Reparar bache";
        ctx.Vm.FechaLimiteSeleccionada = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Local);

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(tareaCreada);
        Assert.Equal(DateTimeKind.Utc, tareaCreada!.FechaLimite!.Value.Kind);
        Assert.Equal(new DateTime(2026, 8, 10), tareaCreada.FechaLimite!.Value.Date);
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
    public void CargarParaVer_ComoAdminConTareaTerminada_NoMuestraCambioDePrioridad()
    {
        // Decisión 14 del spec: un botón que siempre va a fallar con 409 es peor que no
        // tener botón — MuestraCambioPrioridad también considera el estado, no solo el rol.
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Terminada };

        ctx.Vm.CargarParaVer(tarea);

        Assert.False(ctx.Vm.MuestraCambioPrioridad);
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

        Assert.Equal(TareaFormViewModel.MensajeSinPermiso, ctx.Vm.MensajeError);
        Assert.Empty(ctx.Vm.Notas);
    }

    // ── Clasificación en el alta (spec 2026-09-08) ──────────────────────────────

    [Fact]
    public async Task GuardarAsync_ConClasificacionElegida_LaIncluyeEnLaTareaCreada()
    {
        var ctx = Crear();
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Titulo = "Reparar bache";
        ctx.Vm.ClasificacionPanel.ZonaSeleccionada = new Zona { Id = 3, Nombre = "Centro" };

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.CrearAsync(It.Is<Tarea>(t => t.ZonaId == 3)), Times.Once);
    }

    // ── MuestraReclasificar (D9 del spec): NO copia MuestraCambioPrioridad ──────

    [Fact]
    public void MuestraReclasificar_AdminConTareaTerminada_EsTrue()
    {
        // El caso que distingue esta fórmula de MuestraCambioPrioridad (D9): la
        // reclasificación alcanza también tareas terminales.
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Vm.CargarParaVer(new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Terminada });

        Assert.True(ctx.Vm.MuestraReclasificar);
    }

    [Fact]
    public void MuestraReclasificar_OperadorConTareaTerminada_EsFalse()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Vm.CargarParaVer(new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Terminada });

        Assert.False(ctx.Vm.MuestraReclasificar);
    }

    [Fact]
    public void MuestraReclasificar_AdminEnModoAlta_EsFalse()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Vm.CargarParaCrear();

        Assert.False(ctx.Vm.MuestraReclasificar);
    }

    // ── ReclasificarAsync (comando) ──────────────────────────────────────────────

    [Fact]
    public async Task ReclasificarCommand_ModalCancelado_NoLlamaAlServicio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Vm.CargarParaVer(new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente });
        ctx.DialogoClasificacion.Setup(d => d.PedirClasificacionAsync(It.IsAny<DatosClasificacionTarea>()))
            .ReturnsAsync((DatosClasificacionTarea?)null);

        await ctx.Vm.ReclasificarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ReclasificarAsync(It.IsAny<int>(), It.IsAny<DatosClasificacionTarea>()), Times.Never);
    }

    [Fact]
    public async Task ReclasificarCommand_ModalConfirmado_LlamaAlServicioYRefrescaLaTarea()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Vm.CargarParaVer(tarea);

        var nuevaClasificacion = new DatosClasificacionTarea(3, null, null, null, null);
        ctx.DialogoClasificacion.Setup(d => d.PedirClasificacionAsync(It.IsAny<DatosClasificacionTarea>()))
            .ReturnsAsync(nuevaClasificacion);
        ctx.Svc.Setup(s => s.ReclasificarAsync(1, nuevaClasificacion)).Returns(Task.CompletedTask);
        var tareaActualizada = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente,
            ZonaId = 3, Zona = new Zona { Id = 3, Nombre = "Centro" },
        };
        ctx.Svc.Setup(s => s.ObtenerPorIdAsync(1)).ReturnsAsync(tareaActualizada);

        await ctx.Vm.ReclasificarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ReclasificarAsync(1, nuevaClasificacion), Times.Once);
        Assert.Equal("Centro", ctx.Vm.ZonaTexto);
    }
}
