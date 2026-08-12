using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Documentos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Documentos;

public class DocumentoFormViewModelTests
{
    private static DocumentoAdministrativo DocumentoDe(int id, EstadoDocumento estado) => new()
    {
        Id = id, Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow.Date, Descripcion = "Bacheo calle Rivera",
        Estado = estado, RegistradoPorUsuarioId = 1, FechaRegistro = DateTime.UtcNow,
    };

    private static (DocumentoFormViewModel Vm, Mock<IDocumentoAdministrativoService> Svc, Mock<IConfirmacionService> Confirm)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var svc = new Mock<IDocumentoAdministrativoService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Motivo de prueba");
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var adjuntosPanel = new AdjuntosDocumentoPanelViewModel(
            Mock.Of<IAdjuntoDocumentoService>(), Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), confirm.Object, session.Object);

        var vm = new DocumentoFormViewModel(svc.Object, session.Object, nav.Object, confirm.Object, adjuntosPanel);
        return (vm, svc, confirm);
    }

    [Fact]
    public async Task GuardarAsync_ModoAlta_LlamaARegistrarAsyncConLosDatosCargados()
    {
        var ctx = Crear();
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Numero = "0099";
        ctx.Vm.AnioSeleccionado = 2026;
        ctx.Vm.TipoSeleccionado = TipoDocumento.Oficio;
        ctx.Vm.FechaEmisionSeleccionada = new DateTime(2026, 8, 11);
        ctx.Vm.Descripcion = "Pedido de materiales";

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.RegistrarAsync(It.Is<DocumentoAdministrativo>(d =>
            d.Numero == "0099" && d.Anio == 2026 && d.Tipo == TipoDocumento.Oficio
            && d.Descripcion == "Pedido de materiales")), Times.Once);
    }

    [Fact]
    public async Task CargarParaVerAsync_DocumentoActivo_PuedeIniciarSegunElDominio()
    {
        var ctx = Crear();
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        Assert.True(ctx.Vm.PuedeIniciar);
        Assert.True(ctx.Vm.PuedeEditar);
        Assert.False(ctx.Vm.PuedeVolverAPendiente);
    }

    [Fact]
    public async Task CargarParaVerAsync_DocumentoFinalizado_PuedeIniciarEsFalso_AunqueLaTransicionSeaValida()
    {
        // C6, mismo fix que DocumentoFila: Finalizado -> EnProceso es la reapertura, válida en
        // el dominio, así que PuedeTransicionarA(EnProceso) solo no alcanza para gatear el botón.
        var ctx = Crear();
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Finalizado));

        Assert.False(ctx.Vm.PuedeIniciar);
    }

    [Fact]
    public async Task CargarParaVerAsync_RolOperador_PuedeAnularYPuedeReabrirSonFalse()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        Assert.False(ctx.Vm.PuedeAnular);
        Assert.False(ctx.Vm.PuedeReabrir);
    }

    [Fact]
    public async Task CargarParaVerAsync_DocumentoCerrado_PuedeEditarEsFalse()
    {
        var ctx = Crear();
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Finalizado));

        Assert.False(ctx.Vm.PuedeEditar);
    }

    [Fact]
    public async Task AnularAsync_UsuarioCancelaElMotivo_NoLlamaAlServicio()
    {
        var ctx = Crear();
        ctx.Confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        await ctx.Vm.AnularCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AnularAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnularAsync_MotivoEnBlanco_NoLlamaAlServicio()
    {
        var ctx = Crear();
        ctx.Confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("   ");
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        await ctx.Vm.AnularCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AnularAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnularAsync_ConMotivo_LlamaAAnularAsyncConElMotivoTipeado()
    {
        var ctx = Crear();
        ctx.Confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("El interesado no volvió a presentarse.");
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        await ctx.Vm.AnularCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AnularAsync(1, "El interesado no volvió a presentarse."), Times.Once);
    }

    [Fact]
    public async Task ReabrirAsync_ConMotivo_LlamaAReabrirAsyncConElMotivoTipeado()
    {
        var ctx = Crear();
        ctx.Confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Se encontró documentación adicional.");
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Anulado));

        await ctx.Vm.ReabrirCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ReabrirAsync(1, "Se encontró documentación adicional."), Times.Once);
    }

    [Fact]
    public async Task AgregarNotaAsync_LlamaAlServicioYLimpiaElTexto()
    {
        var ctx = Crear();
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));
        ctx.Vm.NuevaNotaTexto = "Falta la firma del interesado.";

        await ctx.Vm.AgregarNotaCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AgregarNotaAsync(1, "Falta la firma del interesado."), Times.Once);
        Assert.Equal(string.Empty, ctx.Vm.NuevaNotaTexto);
    }

    [Fact]
    public async Task IniciarAsync_SesionSinPermiso_NoMuestraMensajeError()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.IniciarProcesoAsync(It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException());
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));

        await ctx.Vm.IniciarCommand.ExecuteAsync(null);

        Assert.Null(ctx.Vm.MensajeError);
    }

    [Fact]
    public async Task FinalizarAsync_ReinicializaElPanelDeAdjuntos_PuedeAgregarPasaAFalse()
    {
        // I4: RecargarAsync() refresca los 7 Puede* del formulario pero no reinicializaba
        // AdjuntosPanel -- el botón "Agregar" adjunto seguía habilitado tras finalizar, y el
        // clic comía un 409 del servidor (el servidor sí valida EsActivo).
        var ctx = Crear();
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Pendiente));
        Assert.True(ctx.Vm.AdjuntosPanel.PuedeAgregar);

        ctx.Svc.Setup(s => s.ObtenerPorIdAsync(1)).ReturnsAsync(DocumentoDe(1, EstadoDocumento.Finalizado));

        await ctx.Vm.FinalizarCommand.ExecuteAsync(null);

        Assert.False(ctx.Vm.AdjuntosPanel.PuedeAgregar);
    }

    [Fact]
    public async Task ReabrirAsync_ReinicializaElPanelDeAdjuntos_PuedeAgregarVuelveATrue()
    {
        // Caso inverso de I4: al revés, reabrir dejaba los botones deshabilitados hasta salir
        // y volver a entrar a la pantalla.
        var ctx = Crear();
        await ctx.Vm.CargarParaVerAsync(DocumentoDe(1, EstadoDocumento.Anulado));
        Assert.False(ctx.Vm.AdjuntosPanel.PuedeAgregar);

        ctx.Svc.Setup(s => s.ObtenerPorIdAsync(1)).ReturnsAsync(DocumentoDe(1, EstadoDocumento.Pendiente));
        ctx.Confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Se encontró documentación adicional.");

        await ctx.Vm.ReabrirCommand.ExecuteAsync(null);

        Assert.True(ctx.Vm.AdjuntosPanel.PuedeAgregar);
    }

    [Fact]
    public void FechaEmisionSeleccionada_AlSetearlaUltima_HabilitaGuardarCommand()
    {
        // F4: PuedeGuardar() exige Numero + Descripcion + FechaEmisionSeleccionada. No se llama
        // a CargarParaCrear() acá porque ya deja FechaEmisionSeleccionada precargada con
        // DateTime.UtcNow.Date -- eso taparía el hallazgo (la propiedad no llevaba
        // [NotifyCanExecuteChangedFor], a diferencia de Numero/Descripcion que sí).
        var ctx = Crear();
        ctx.Vm.Numero = "0099";
        ctx.Vm.Descripcion = "Pedido de materiales";
        Assert.False(ctx.Vm.GuardarCommand.CanExecute(null));

        ctx.Vm.FechaEmisionSeleccionada = new DateTime(2026, 8, 11);

        Assert.True(ctx.Vm.GuardarCommand.CanExecute(null));
    }
}
