using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Documentos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Documentos;

public class AdjuntosDocumentoPanelViewModelTests
{
    private static AdjuntoDocumentoDto AdjuntoDe(int id) =>
        new(id, 1, "factura.pdf", "application/pdf", 1024, DateTime.UtcNow);

    private static (AdjuntosDocumentoPanelViewModel Vm, Mock<IAdjuntoDocumentoService> Svc,
        Mock<IServicioSeleccionArchivo> Seleccion, Mock<IConfirmacionService> Confirm)
        Crear(RolUsuario rol = RolUsuario.Operador)
    {
        var svc = new Mock<IAdjuntoDocumentoService>();
        svc.Setup(s => s.ListarPorDocumentoAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<AdjuntoDocumentoDto>());

        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var apertura = new Mock<IServicioAperturaArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);

        var vm = new AdjuntosDocumentoPanelViewModel(svc.Object, seleccion.Object, apertura.Object, confirm.Object, session.Object);
        return (vm, svc, seleccion, confirm);
    }

    [Fact]
    public async Task InicializarAsync_CargaLosAdjuntosDelDocumento()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarPorDocumentoAsync(5))
            .ReturnsAsync(new List<AdjuntoDocumentoDto> { AdjuntoDe(1), AdjuntoDe(2) });

        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        Assert.Equal(2, ctx.Vm.Items.Count);
    }

    /// <summary>Servidor caído al listar: queda en SinConexion (la vista muestra el error con Reintentar), sin modal global,
    /// y Reintentar vuelve a consultar. No es lo mismo que "no hay adjuntos".</summary>
    [Fact]
    public async Task InicializarAsync_ServidorCaido_MarcaSinConexionSinModal_YReintentarRecarga()
    {
        var ctx = Crear();
        ctx.Svc.SetupSequence(s => s.ListarPorDocumentoAsync(5))
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(new List<AdjuntoDocumentoDto> { AdjuntoDe(1) });

        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        Assert.True(ctx.Vm.SinConexion);
        Assert.Empty(ctx.Vm.Items);
        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);

        await ctx.Vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(ctx.Vm.SinConexion);
        Assert.Single(ctx.Vm.Items);
    }

    /// <summary>Reintentar tras un servidor caído pasa por el mismo catch que la carga inicial: si ahora el listado falla
    /// por negocio (404/500) el error va al modal y no se escapa del comando hacia el UnhandledException.</summary>
    [Fact]
    public async Task Reintentar_TrasServidorCaido_ConErrorDeNegocio_VaAlModalSinEscaparse()
    {
        var ctx = Crear();
        ctx.Svc.SetupSequence(s => s.ListarPorDocumentoAsync(5))
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ThrowsAsync(new ArgumentException("boom"));
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);
        Assert.True(ctx.Vm.SinConexion);

        await ctx.Vm.ReintentarCargaCommand.ExecuteAsync(null);

        ctx.Confirm.Verify(c => c.InformarAsync("boom"), Times.Once);
        Assert.False(ctx.Vm.SinConexion);
    }

    [Fact]
    public async Task InicializarAsync_Prohibido403_MarcaSinPermisoSinModal()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarPorDocumentoAsync(5)).ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        Assert.True(ctx.Vm.SinPermiso);
        Assert.Equal("No tenés permiso para ver los adjuntos.", ctx.Vm.MensajeSinPermiso);
        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InicializarAsync_ErrorDeNegocio_SigueYendoAlModal_YNoMarcaSinConexion()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarPorDocumentoAsync(5)).ThrowsAsync(new ArgumentException("boom"));

        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        Assert.False(ctx.Vm.SinConexion);
        ctx.Confirm.Verify(c => c.InformarAsync("boom"), Times.Once);
    }

    [Fact]
    public async Task InicializarAsync_RolOperadorDocumentoActivo_PuedeAgregarTruePuedeQuitarFalse()
    {
        var ctx = Crear(rol: RolUsuario.Operador);

        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        Assert.True(ctx.Vm.PuedeAgregar);
        Assert.False(ctx.Vm.PuedeQuitar);
    }

    [Fact]
    public async Task InicializarAsync_RolAdminDocumentoActivo_PuedeAgregarYPuedeQuitarTrue()
    {
        var ctx = Crear(rol: RolUsuario.Admin);

        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        Assert.True(ctx.Vm.PuedeAgregar);
        Assert.True(ctx.Vm.PuedeQuitar);
    }

    [Fact]
    public async Task InicializarAsync_DocumentoCerrado_PuedeAgregarYPuedeQuitarFalse_AunSiendoAdmin()
    {
        // D11(a): la regla corta en ambos sentidos sobre un documento cerrado, sin importar el rol.
        var ctx = Crear(rol: RolUsuario.Admin);

        await ctx.Vm.InicializarAsync(5, documentoActivo: false);

        Assert.False(ctx.Vm.PuedeAgregar);
        Assert.False(ctx.Vm.PuedeQuitar);
    }

    [Fact]
    public async Task AgregarAsync_UsuarioCancelaLaSeleccion_NoLlamaAlServicio()
    {
        var ctx = Crear();
        ctx.Seleccion.Setup(s => s.SeleccionarArchivoAsync())
            .ReturnsAsync(((string, byte[])?)null);
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.AgregarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AgregarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task AgregarAsync_ArchivoSeleccionado_LlamaAlServicioYRecarga()
    {
        var ctx = Crear();
        var contenido = new byte[] { 1, 2, 3 };
        ctx.Seleccion.Setup(s => s.SeleccionarArchivoAsync())
            .ReturnsAsync(("factura.pdf", contenido));
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.AgregarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AgregarAsync(5, "factura.pdf", contenido), Times.Once);
    }

    [Fact]
    public async Task QuitarAsync_LlamaAlServicioYRecarga()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.QuitarCommand.ExecuteAsync(AdjuntoDe(9));

        ctx.Svc.Verify(s => s.QuitarAsync(9), Times.Once);
    }

    [Fact]
    public async Task AgregarAsync_ErrorDeNegocio_InformaAlUsuario()
    {
        var ctx = Crear();
        ctx.Seleccion.Setup(s => s.SeleccionarArchivoAsync())
            .ReturnsAsync(("factura.pdf", new byte[] { 1 }));
        ctx.Svc.Setup(s => s.AgregarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>()))
            .ThrowsAsync(new StockApp.Domain.Exceptions.ReglaDeNegocioException("El documento está cerrado."));
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.AgregarCommand.ExecuteAsync(null);

        ctx.Confirm.Verify(c => c.InformarAsync("El documento está cerrado."), Times.Once);
    }

    [Fact]
    public async Task QuitarAsync_SesionSinPermiso_NoInformaAlUsuario()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Svc.Setup(s => s.QuitarAsync(It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException());
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.QuitarCommand.ExecuteAsync(AdjuntoDe(9));

        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Quitar_SinConfirmar_NoLlamaAlServicio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.QuitarCommand.ExecuteAsync(AdjuntoDe(9));

        ctx.Svc.Verify(s => s.QuitarAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Quitar_Confirmado_QuitaYRecarga()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var adjunto = new AdjuntoDocumentoDto(9, 5, "oficio.pdf", "application/pdf", 1024, DateTime.UtcNow);
        await ctx.Vm.InicializarAsync(5, documentoActivo: true);

        await ctx.Vm.QuitarCommand.ExecuteAsync(adjunto);

        Assert.Equal("¿Quitar el adjunto \"oficio.pdf\"?", AdjuntosDocumentoPanelViewModel.MensajeConfirmarQuitar("oficio.pdf"));
        ctx.Confirm.Verify(c => c.PreguntarAsync("¿Quitar el adjunto \"oficio.pdf\"?"), Times.Once);
        ctx.Svc.Verify(s => s.QuitarAsync(9), Times.Once);
        ctx.Svc.Verify(s => s.ListarPorDocumentoAsync(5), Times.Exactly(2));
    }
}
