using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Documentos;
using StockApp.Application.Proyectos;
using StockApp.Application.Tareas;
using StockApp.ApiClient;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

public class ClasificacionTareaPanelViewModelTests
{
    private static (ClasificacionTareaPanelViewModel Vm, Mock<IProyectoService> Proyectos,
                     Mock<IDocumentoAdministrativoService> Documentos, Mock<IConfirmacionService> Confirm)
        Crear()
    {
        var proyectos = new Mock<IProyectoService>();
        var documentos = new Mock<IDocumentoAdministrativoService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        proyectos.Setup(p => p.ListarOpcionesAsync()).ReturnsAsync(new List<OpcionProyectoDto>
        {
            new(1, "Bacheo calle Uruguay", EstadoProyecto.EnCurso),
            new(2, "Plaza de deportes", EstadoProyecto.Pendiente),
        });

        var vm = new ClasificacionTareaPanelViewModel(proyectos.Object, documentos.Object, confirm.Object);
        return (vm, proyectos, documentos, confirm);
    }

    [Fact]
    public async Task InicializarAsync_ExponeProyectosYExpediente_SinClasificadores()
    {
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(9)).ReturnsAsync(new DocumentoAdministrativo
        {
            Id = 9, Numero = "0001", Anio = 2026, Tipo = TipoDocumento.Expediente, Descripcion = "x",
        });

        await ctx.Vm.InicializarAsync(new DatosClasificacionTarea(2, 9));

        // El centinela "(ninguno)" primero y después los proyectos abiertos, con su título.
        Assert.Equal(new[] { "(ninguno)", "Bacheo calle Uruguay", "Plaza de deportes" },
            ctx.Vm.ProyectosDisponibles.Select(o => o.Nombre));
        Assert.Null(ctx.Vm.ProyectosDisponibles[0].Id);
        Assert.Equal(2, ctx.Vm.ProyectoSeleccionado?.Id);
        Assert.Equal(9, ctx.Vm.DocumentoSeleccionado?.Id);
        // Sin clasificadores: el panel ya no expone zona, dimensión, organismo ni origen.
        var propiedades = typeof(ClasificacionTareaPanelViewModel).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(propiedades, n => n.StartsWith("Zona") || n.StartsWith("Dimension")
            || n.StartsWith("Organismo") || n.StartsWith("Origen"));
        Assert.Equal(new DatosClasificacionTarea(2, 9), ctx.Vm.ObtenerDatos());
    }

    [Fact]
    public async Task InicializarAsync_ProyectoActualCerrado_ApareceComoOpcion()
    {
        var ctx = Crear();

        // El proyecto 7 de la tarea ya está Terminado: ListarOpcionesAsync no lo trae.
        await ctx.Vm.InicializarAsync(new DatosClasificacionTarea(7, null), "Puente arroyo Las Vacas");

        var opcion = Assert.Single(ctx.Vm.ProyectosDisponibles, o => o.Id == 7);
        Assert.Equal("Puente arroyo Las Vacas", opcion.Nombre);
        Assert.Same(opcion, ctx.Vm.ProyectoSeleccionado);
        // Guardar sin tocar el combo conserva el vínculo.
        Assert.Equal(7, ctx.Vm.ObtenerDatos().ProyectoId);
    }

    [Fact]
    public async Task InicializarAsync_SinValoresActuales_SeleccionaElCentinela()
    {
        var ctx = Crear();

        await ctx.Vm.InicializarAsync();

        Assert.Equal(3, ctx.Vm.ProyectosDisponibles.Count);
        Assert.Null(ctx.Vm.ProyectoSeleccionado?.Id);
        Assert.Null(ctx.Vm.DocumentoSeleccionado);
    }

    [Fact]
    public async Task ObtenerDatos_EligiendoElCentinelaDelComboReal_DesvinculaElProyecto()
    {
        var ctx = Crear();
        await ctx.Vm.InicializarAsync(new DatosClasificacionTarea(1, null));
        Assert.Equal(1, ctx.Vm.ProyectoSeleccionado?.Id); // precondición: llegó precargado

        ctx.Vm.ProyectoSeleccionado = ctx.Vm.ProyectosDisponibles.First(o => o.Id is null);

        Assert.Null(ctx.Vm.ObtenerDatos().ProyectoId);
    }

    [Fact]
    public void ObtenerDatos_SinSeleccion_DevuelveAmbosCamposEnNull()
    {
        var ctx = Crear();

        Assert.Equal(new DatosClasificacionTarea(null, null), ctx.Vm.ObtenerDatos());
    }

    [Fact]
    public async Task BuscarExpedientesAsync_TextoCortoDelMinimo_NoConsultaAlServicio()
    {
        var ctx = Crear();

        var resultado = await ctx.Vm.BuscarExpedientesAsync("ab", CancellationToken.None);

        Assert.Empty(resultado);
        ctx.Documentos.Verify(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    [Fact]
    public async Task BuscarExpedientesAsync_TextoValido_FiltraPorTipoExpediente()
    {
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ReturnsAsync(new List<DocumentoAdministrativo>
            {
                new() { Id = 1, Numero = "0001", Tipo = TipoDocumento.Expediente, Descripcion = "x" },
            });

        var resultado = await ctx.Vm.BuscarExpedientesAsync("obra", CancellationToken.None);

        Assert.Single(resultado);
        ctx.Documentos.Verify(d => d.ListarActivosAsync(
            It.Is<FiltroDocumentos>(f => f.Tipo == TipoDocumento.Expediente && f.Texto == "obra")), Times.Once);
    }

    [Fact]
    public async Task BuscarExpedientesAsync_MasResultadosQueElTope_AcotaYAvisa()
    {
        var ctx = Crear();
        var muchos = Enumerable.Range(1, 25)
            .Select(i => new DocumentoAdministrativo { Id = i, Numero = $"{i:0000}", Tipo = TipoDocumento.Expediente, Descripcion = "x" })
            .ToList();
        ctx.Documentos.Setup(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>())).ReturnsAsync(muchos);

        var resultado = await ctx.Vm.BuscarExpedientesAsync("expediente", CancellationToken.None);

        Assert.Equal(20, resultado.Count());
        Assert.NotNull(ctx.Vm.MensajeBuscadorExpediente);
    }

    [Fact]
    public async Task BuscarExpedientesAsync_SinPermisoDeDocumentos_DegradaSinRomperElRestoDelPanel()
    {
        var ctx = Crear();
        await ctx.Vm.InicializarAsync();
        ctx.Vm.ProyectoSeleccionado = ctx.Vm.ProyectosDisponibles.First(o => o.Id == 2);
        ctx.Documentos.Setup(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var resultado = await ctx.Vm.BuscarExpedientesAsync("obra", CancellationToken.None);

        Assert.Empty(resultado);
        Assert.NotNull(ctx.Vm.MensajeBuscadorExpediente);

        var datos = ctx.Vm.ObtenerDatos();
        Assert.Equal(2, datos.ProyectoId);
        Assert.Null(datos.DocumentoAdministrativoId);
    }

    // ── InicializarAsync sin try/catch (revisión final, Important 2) ────────────
    // TareaFormView.axaml.cs dispara InicializarAsync desde un handler async void
    // (DataContextChanged): una excepción no atrapada acá no la agarra nadie y termina en
    // crash.log. Molde EXACTO de AdjuntosDocumentoPanelViewModelTests
    // (AgregarAsync_ErrorDeNegocio_InformaAlUsuario / QuitarAsync_SesionSinPermiso_NoInformaAlUsuario).

    [Fact]
    public async Task InicializarAsync_FallaElServicio_NoPropagaYInformaConElMensajeReal()
    {
        var ctx = Crear();
        ctx.Proyectos.Setup(p => p.ListarOpcionesAsync())
            .ThrowsAsync(new ServidorNoDisponibleException());

        // No debe lanzar -- verificado por mutación: sacando el try/catch de InicializarAsync
        // esta llamada revienta con ServidorNoDisponibleException y el test se pone rojo.
        await ctx.Vm.InicializarAsync();

        ctx.Confirm.Verify(c => c.InformarAsync(ServidorNoDisponibleException.MensajePorDefecto), Times.Once);
    }

    [Fact]
    public async Task InicializarAsync_SesionSinPermiso_NoPropagaYNoInformaSinPermiso()
    {
        // El mensaje no debe mentir sobre la causa: un UnauthorizedAccessException ya se avisó
        // aparte (AuthTokenHandler/App.axaml.cs) -- este catch NO debe agregar un "sin permiso"
        // propio, mismo criterio que AdjuntosDocumentoPanelViewModel.ManejarErrorAsync.
        var ctx = Crear();
        ctx.Proyectos.Setup(p => p.ListarOpcionesAsync()).ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.InicializarAsync();

        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }
}
