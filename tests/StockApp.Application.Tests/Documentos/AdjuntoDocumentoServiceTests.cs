using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Auth;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Application.Tests.Documentos;

public class AdjuntoDocumentoServiceTests
{
    private readonly Mock<IAdjuntoDocumentoRepository> _adjuntos = new();
    private readonly Mock<IDocumentoAdministrativoRepository> _documentos = new();
    private readonly Mock<ICurrentSession> _session = new();
    private readonly Mock<IAuthorizationService> _auth = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly AdjuntoDocumentoService _service;

    private static readonly byte[] BytesPdf = { 0x25, 0x50, 0x44, 0x46, 0x01 };

    private static DocumentoAdministrativo NuevoDocumento(EstadoDocumento estado = EstadoDocumento.Pendiente) => new()
    {
        Id = 1, Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = new DateTime(2026, 1, 15), Descripcion = "Solicitud de poda de árbol", Estado = estado,
    };

    public AdjuntoDocumentoServiceTests()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        _session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));
        _auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));
        _documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(NuevoDocumento(EstadoDocumento.Pendiente));

        _service = new AdjuntoDocumentoService(
            _adjuntos.Object, _documentos.Object, _session.Object, _auth.Object, _audit.Object);
    }

    // ── AgregarAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        _auth.Setup(a => a.Verificar(_session.Object, Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.AgregarAsync(1, "factura.pdf", BytesPdf));

        _adjuntos.Verify(r => r.AgregarAsync(It.IsAny<AdjuntoDocumento>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task AgregarAsync_DocumentoInexistente_LanzaEntidadNoEncontrada()
    {
        _documentos.Setup(d => d.ObtenerPorIdAsync(99)).ReturnsAsync((DocumentoAdministrativo?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => _service.AgregarAsync(99, "factura.pdf", BytesPdf));
    }

    [Fact]
    public async Task AgregarAsync_DocumentoCerrado_LanzaReglaDeNegocio()
    {
        _documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(NuevoDocumento(EstadoDocumento.Finalizado));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _service.AgregarAsync(1, "factura.pdf", BytesPdf));

        _adjuntos.Verify(r => r.AgregarAsync(It.IsAny<AdjuntoDocumento>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task AgregarAsync_MimeNoPermitido_LanzaReglaDeNegocio()
    {
        var bytesInvalidos = new byte[] { 0x00, 0x01, 0x02 };

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _service.AgregarAsync(1, "archivo.exe", bytesInvalidos));
    }

    [Fact]
    public async Task AgregarAsync_Exitoso_GeneraEventoAutomaticoEnElDocumento()
    {
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<AdjuntoDocumento>(), It.IsAny<byte[]>())).ReturnsAsync(10);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        _documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await _service.AgregarAsync(1, "factura.pdf", BytesPdf);

        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Contains("factura.pdf", evento.Texto);
        _documentos.Verify(d => d.ActualizarAsync(documento), Times.Once);
    }

    [Fact]
    public async Task AgregarAsync_Exitoso_RegistraAuditoria()
    {
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<AdjuntoDocumento>(), It.IsAny<byte[]>())).ReturnsAsync(10);

        await _service.AgregarAsync(1, "factura.pdf", BytesPdf);

        _audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.AltaAdjuntoDocumento, "AdjuntoDocumento", 10, It.IsAny<string>()), Times.Once);
    }

    // ── ListarPorDocumentoAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ListarPorDocumentoAsync_SinPermiso_LanzaExcepcion()
    {
        _auth.Setup(a => a.Verificar(_session.Object, Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ListarPorDocumentoAsync(1));
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_DelegaAlRepo()
    {
        _adjuntos.Setup(r => r.ListarPorDocumentoAsync(1)).ReturnsAsync(new List<AdjuntoDocumento>
        {
            new() { Id = 1, DocumentoAdministrativoId = 1, NombreArchivo = "a.pdf", ContentType = "application/pdf", Activo = true },
        });

        var resultado = await _service.ListarPorDocumentoAsync(1);

        Assert.Single(resultado);
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_NoIncluyeAdjuntosDadosDeBaja()
    {
        // El filtro por Activo vive en el repositorio (AdjuntoDocumentoRepository.ListarPorDocumentoAsync,
        // mismo criterio que AdjuntoRepository de Finanzas) — el service confía en ese contrato y NO
        // vuelve a filtrar acá. El mock devuelve solo el adjunto activo, como haría el repo real
        // post-fix; la cobertura de la exclusión en sí vive en el test de integración
        // AdjuntoDocumentoRepositoryTests.ListarPorDocumentoAsync_ExcluyeAdjuntosInactivos.
        _adjuntos.Setup(r => r.ListarPorDocumentoAsync(1)).ReturnsAsync(new List<AdjuntoDocumento>
        {
            new() { Id = 1, DocumentoAdministrativoId = 1, NombreArchivo = "activo.pdf", ContentType = "application/pdf", Activo = true },
        });

        var resultado = await _service.ListarPorDocumentoAsync(1);

        var unico = Assert.Single(resultado);
        Assert.Equal("activo.pdf", unico.NombreArchivo);
    }

    // ── ObtenerContenidoAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerContenidoAsync_AdjuntoInexistente_LanzaEntidadNoEncontrada()
    {
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((AdjuntoDocumento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => _service.ObtenerContenidoAsync(99));
    }

    [Fact]
    public async Task ObtenerContenidoAsync_AdjuntoDadoDeBaja_LanzaEntidadNoEncontrada()
    {
        var adjunto = new AdjuntoDocumento { Id = 7, DocumentoAdministrativoId = 1, Activo = false, NombreArchivo = "a.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => _service.ObtenerContenidoAsync(7));

        _adjuntos.Verify(r => r.ObtenerContenidoAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_AdjuntoActivo_DevuelveContenido()
    {
        var adjunto = new AdjuntoDocumento
        {
            Id = 7, DocumentoAdministrativoId = 1, Activo = true,
            NombreArchivo = "a.pdf", ContentType = "application/pdf",
        };
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);
        _adjuntos.Setup(r => r.ObtenerContenidoAsync(7)).ReturnsAsync(bytes);

        var resultado = await _service.ObtenerContenidoAsync(7);

        Assert.Equal(bytes, resultado.Contenido);
        Assert.Equal("a.pdf", resultado.NombreArchivo);
    }

    // ── QuitarAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task QuitarAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        _auth.Setup(a => a.Verificar(It.Is<ICurrentSession>(s => s.RolActual == RolUsuario.Operador), Permisos.AdministrarDocumentos))
            .Throws<UnauthorizedAccessException>();
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.QuitarAsync(7));

        _adjuntos.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task QuitarAsync_AdjuntoInexistente_LanzaEntidadNoEncontrada()
    {
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((AdjuntoDocumento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => _service.QuitarAsync(99));
    }

    [Fact]
    public async Task QuitarAsync_DocumentoCerrado_LanzaReglaDeNegocio()
    {
        // D11(a): la regla corta en ambos sentidos, agregar Y quitar.
        var adjunto = new AdjuntoDocumento { Id = 7, DocumentoAdministrativoId = 1, Activo = true, NombreArchivo = "a.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);
        _documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(NuevoDocumento(EstadoDocumento.Anulado));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => _service.QuitarAsync(7));

        _adjuntos.Verify(r => r.ActualizarAsync(It.IsAny<AdjuntoDocumento>()), Times.Never);
    }

    [Fact]
    public async Task QuitarAsync_ComoAdmin_HaceBajaLogicaYGeneraEventoEnElDocumento()
    {
        var adjunto = new AdjuntoDocumento { Id = 7, DocumentoAdministrativoId = 1, Activo = true, NombreArchivo = "a.pdf" };
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);
        _documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await _service.QuitarAsync(7);

        Assert.False(adjunto.Activo);
        _adjuntos.Verify(r => r.ActualizarAsync(adjunto), Times.Once);
        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Contains("a.pdf", evento.Texto);
    }

    [Fact]
    public async Task QuitarAsync_RegistraAuditoria()
    {
        var adjunto = new AdjuntoDocumento { Id = 7, DocumentoAdministrativoId = 1, Activo = true, NombreArchivo = "a.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);

        await _service.QuitarAsync(7);

        _audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.BajaAdjuntoDocumento, "AdjuntoDocumento", 7, It.IsAny<string>()), Times.Once);
    }

    // ── Superficie de la interfaz (append-only, whitelist explícita) ─────────

    [Fact]
    public void IAdjuntoDocumentoService_ExponeExactamenteLosMetodosEsperados()
    {
        var esperados = new HashSet<string>
        {
            nameof(IAdjuntoDocumentoService.AgregarAsync),
            nameof(IAdjuntoDocumentoService.ListarPorDocumentoAsync),
            nameof(IAdjuntoDocumentoService.ObtenerContenidoAsync),
            nameof(IAdjuntoDocumentoService.QuitarAsync),
        };

        var metodos = typeof(IAdjuntoDocumentoService).GetMethods().Select(m => m.Name).ToHashSet();

        Assert.Equal(esperados, metodos);
    }
}
