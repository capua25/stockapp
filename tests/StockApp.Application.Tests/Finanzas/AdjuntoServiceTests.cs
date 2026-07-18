using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

public class AdjuntoServiceTests
{
    private readonly Mock<IAdjuntoRepository> _adjuntos = new();
    private readonly Mock<IGastoRepository> _gastos = new();
    private readonly Mock<ICurrentSession> _session = new();
    private readonly Mock<IAuthorizationService> _auth = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly AdjuntoService _service;

    private static readonly byte[] BytesPdf = { 0x25, 0x50, 0x44, 0x46, 0x01 };

    public AdjuntoServiceTests()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        _session.Setup(s => s.UsuarioActual).Returns(new StockApp.Application.Auth.UsuarioSesion(1, "admin", RolUsuario.Admin, null));
        _gastos.Setup(g => g.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new Gasto { Id = 1, Activo = true });

        _service = new AdjuntoService(_adjuntos.Object, _gastos.Object, _session.Object, _auth.Object, _audit.Object);
    }

    [Fact]
    public async Task AgregarAGastoAsync_VerificaPermisoRegistrarGastos()
    {
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<Adjunto>(), It.IsAny<byte[]>())).ReturnsAsync(10);

        await _service.AgregarAGastoAsync(1, "factura.pdf", BytesPdf);

        _auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.RegistrarGastos), Times.Once);
    }

    [Fact]
    public async Task AgregarAGastoAsync_GastoInexistente_LanzaEntidadNoEncontrada()
    {
        _gastos.Setup(g => g.ObtenerPorIdAsync(99)).ReturnsAsync((Gasto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => _service.AgregarAGastoAsync(99, "factura.pdf", BytesPdf));
    }

    [Fact]
    public async Task AgregarAGastoAsync_MimeNoPermitido_LanzaReglaDeNegocio()
    {
        var bytesInvalidos = new byte[] { 0x00, 0x01, 0x02 };

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _service.AgregarAGastoAsync(1, "archivo.exe", bytesInvalidos));
    }

    [Fact]
    public async Task AgregarAGastoAsync_Exitoso_RegistraAuditoria()
    {
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<Adjunto>(), It.IsAny<byte[]>())).ReturnsAsync(10);

        await _service.AgregarAGastoAsync(1, "factura.pdf", BytesPdf);

        _audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.AltaAdjunto, "Adjunto", 10, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AgregarAGastoAsync_SeteaGastoIdYPagoGastoIdNull()
    {
        Adjunto? capturado = null;
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<Adjunto>(), It.IsAny<byte[]>()))
            .Callback<Adjunto, byte[]>((a, _) => capturado = a)
            .ReturnsAsync(10);

        await _service.AgregarAGastoAsync(1, "factura.pdf", BytesPdf);

        Assert.NotNull(capturado);
        Assert.Equal(1, capturado!.GastoId);
        Assert.Null(capturado.PagoGastoId);
    }

    [Fact]
    public async Task AgregarAGastoAsync_SinPermiso_NoPersisteNiAuditaYPropagaExcepcion()
    {
        _auth.Setup(a => a.Verificar(RolUsuario.Admin, Permisos.RegistrarGastos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.AgregarAGastoAsync(1, "factura.pdf", BytesPdf));

        _adjuntos.Verify(r => r.AgregarAsync(It.IsAny<Adjunto>(), It.IsAny<byte[]>()), Times.Never);
        _audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task AgregarAPagoAsync_VerificaPermisoRegistrarPagos()
    {
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<Adjunto>(), It.IsAny<byte[]>())).ReturnsAsync(11);

        await _service.AgregarAPagoAsync(5, "recibo.pdf", BytesPdf);

        _auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.RegistrarPagos), Times.Once);
    }

    [Fact]
    public async Task ListarPorGastoAsync_VerificaPermisoVerFinanzas()
    {
        _adjuntos.Setup(r => r.ListarPorGastoAsync(1)).ReturnsAsync(new List<Adjunto>());

        await _service.ListarPorGastoAsync(1);

        _auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.VerFinanzas), Times.Once);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_AdjuntoInexistente_LanzaEntidadNoEncontrada()
    {
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Adjunto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => _service.ObtenerContenidoAsync(99));
    }

    [Fact]
    public async Task ObtenerContenidoAsync_AdjuntoDadoDeBaja_LanzaEntidadNoEncontrada()
    {
        var adjunto = new Adjunto { Id = 7, GastoId = 1, Activo = false, NombreArchivo = "a.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => _service.ObtenerContenidoAsync(7));

        _adjuntos.Verify(r => r.ObtenerContenidoAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_AdjuntoActivo_DevuelveContenido()
    {
        var adjunto = new Adjunto { Id = 7, GastoId = 1, Activo = true, NombreArchivo = "a.pdf", ContentType = "application/pdf" };
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);
        _adjuntos.Setup(r => r.ObtenerContenidoAsync(7)).ReturnsAsync(bytes);

        var resultado = await _service.ObtenerContenidoAsync(7);

        Assert.Equal(bytes, resultado.Contenido);
        Assert.Equal("a.pdf", resultado.NombreArchivo);
    }

    [Fact]
    public async Task QuitarAsync_HaceBajaLogicaYAuditoria()
    {
        var adjunto = new Adjunto { Id = 7, GastoId = 1, Activo = true, NombreArchivo = "a.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);

        await _service.QuitarAsync(7);

        Assert.False(adjunto.Activo);
        _adjuntos.Verify(r => r.ActualizarAsync(adjunto), Times.Once);
        _audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.BajaAdjunto, "Adjunto", 7, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task QuitarAsync_DeUnPago_VerificaPermisoRegistrarPagos()
    {
        var adjunto = new Adjunto { Id = 8, PagoGastoId = 5, Activo = true, NombreArchivo = "r.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(8)).ReturnsAsync(adjunto);

        await _service.QuitarAsync(8);

        _auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.RegistrarPagos), Times.Once);
    }

    [Fact]
    public async Task QuitarAsync_DeUnPago_SinPermisoRegistrarPagos_NoDaDeBajaNiAudita()
    {
        var adjunto = new Adjunto { Id = 9, PagoGastoId = 5, Activo = true, NombreArchivo = "r2.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(9)).ReturnsAsync(adjunto);
        _auth.Setup(a => a.Verificar(RolUsuario.Admin, Permisos.RegistrarPagos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.QuitarAsync(9));

        Assert.True(adjunto.Activo);
        _adjuntos.Verify(r => r.ActualizarAsync(It.IsAny<Adjunto>()), Times.Never);
        _audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }
}
