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

public class DocumentoAdministrativoServiceTests
{
    private static (DocumentoAdministrativoService Svc, Mock<IDocumentoAdministrativoRepository> Repo,
                     Mock<ICurrentSession> Session, Mock<IAuthorizationService> Auth, Mock<IAuditLogger> Audit)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1, string nombreUsuario = "admin.test")
    {
        var repo    = new Mock<IDocumentoAdministrativoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthorizationService>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, nombreUsuario, rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));

        var svc = new DocumentoAdministrativoService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, session, auth, audit);
    }

    private static DocumentoAdministrativo NuevoDocumento(EstadoDocumento estado = EstadoDocumento.Pendiente) => new()
    {
        Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = new DateTime(2026, 1, 15), Descripcion = "Solicitud de poda de árbol", Estado = estado,
    };

    // ── RegistrarAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.RegistrarAsync(NuevoDocumento()));

        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_NumeroVacio_LanzaArgumentException()
    {
        var ctx = Crear();
        var documento = NuevoDocumento();
        documento.Numero = "   ";

        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.RegistrarAsync(documento));
    }

    [Fact]
    public async Task RegistrarAsync_DescripcionVacia_LanzaArgumentException()
    {
        var ctx = Crear();
        var documento = NuevoDocumento();
        documento.Descripcion = "   ";

        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.RegistrarAsync(documento));
    }

    [Fact]
    public async Task RegistrarAsync_NumeroDuplicado_LanzaReglaDeNegocioSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(TipoDocumento.Expediente, 2026, "0087", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.RegistrarAsync(NuevoDocumento()));

        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_DatosValidos_DelegaAlRepoYDevuelveId()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(It.IsAny<TipoDocumento>(), It.IsAny<int>(), It.IsAny<string>(), null))
            .ReturnsAsync(false);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>())).ReturnsAsync(42);

        var id = await ctx.Svc.RegistrarAsync(NuevoDocumento());

        Assert.Equal(42, id);
        ctx.Repo.Verify(r => r.AgregarAsync(It.Is<DocumentoAdministrativo>(d =>
            d.Numero == "0087" && d.Estado == EstadoDocumento.Pendiente)), Times.Once);
    }

    [Fact]
    public async Task RegistrarAsync_SeteaRegistradoPorYFechaRegistroDesdeLaSesionNoDelInput()
    {
        var ctx = Crear(idSesion: 7);
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(It.IsAny<TipoDocumento>(), It.IsAny<int>(), It.IsAny<string>(), null))
            .ReturnsAsync(false);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>())).ReturnsAsync(1);
        var documento = NuevoDocumento();
        documento.RegistradoPorUsuarioId = 999; // valor espurio: el servicio debe ignorarlo

        await ctx.Svc.RegistrarAsync(documento);

        Assert.Equal(7, documento.RegistradoPorUsuarioId);
        Assert.True((DateTime.UtcNow - documento.FechaRegistro) < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RegistrarAsync_SiembraEventoInicialAutomatico()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(It.IsAny<TipoDocumento>(), It.IsAny<int>(), It.IsAny<string>(), null))
            .ReturnsAsync(false);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>())).ReturnsAsync(1);
        var documento = NuevoDocumento();

        await ctx.Svc.RegistrarAsync(documento);

        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
    }

    [Fact]
    public async Task RegistrarAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 3);
        ctx.Repo.Setup(r => r.ExisteNumeroAsync(It.IsAny<TipoDocumento>(), It.IsAny<int>(), It.IsAny<string>(), null))
            .ReturnsAsync(false);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<DocumentoAdministrativo>())).ReturnsAsync(9);

        await ctx.Svc.RegistrarAsync(NuevoDocumento());

        ctx.Audit.Verify(a => a.RegistrarAsync(
            3, AccionAuditada.AltaDocumentoAdministrativo, "DocumentoAdministrativo", 9, It.IsAny<string>()),
            Times.Once);
    }

    // ── ListarActivosAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ListarActivosAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.ListarActivosAsync(new FiltroDocumentos(null, null, null, null)));
    }

    [Fact]
    public async Task ListarActivosAsync_DelegaEnRepoListarActivosAsync()
    {
        // El filtrado Pendiente/EnProceso va en el SQL (Task 5, IDocumentoAdministrativoRepository.
        // ListarActivosAsync), no en memoria: el servicio delega el filtro tal cual, sin volver a
        // filtrar por EsActivo sobre lo que devuelve el repo.
        var ctx = Crear();
        var pendiente = NuevoDocumento(EstadoDocumento.Pendiente);
        var enProceso = NuevoDocumento(EstadoDocumento.EnProceso);
        var filtro = new FiltroDocumentos(null, null, null, null);
        ctx.Repo.Setup(r => r.ListarActivosAsync(filtro))
            .ReturnsAsync(new List<DocumentoAdministrativo> { pendiente, enProceso });

        var resultado = await ctx.Svc.ListarActivosAsync(filtro);

        Assert.Equal(2, resultado.Count);
        ctx.Repo.Verify(r => r.ListarActivosAsync(filtro), Times.Once);
        ctx.Repo.Verify(r => r.ListarCerradosAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    // ── ListarHistorialAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ListarHistorialAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.ListarHistorialAsync(new FiltroDocumentos(null, 2026, null, null)));
    }

    [Fact]
    public async Task ListarHistorialAsync_AnioNulo_LanzaArgumentException()
    {
        // D9: es un request mal formado (400), no un ReglaDeNegocioException (409).
        var ctx = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => ctx.Svc.ListarHistorialAsync(new FiltroDocumentos(null, null, null, null)));

        ctx.Repo.Verify(r => r.ListarCerradosAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    [Fact]
    public async Task ListarHistorialAsync_DelegaEnRepoListarCerradosAsync()
    {
        // Mismo criterio que ListarActivosAsync: el filtrado Finalizado/Anulado va en el SQL
        // (Task 5, IDocumentoAdministrativoRepository.ListarCerradosAsync), no en memoria.
        var ctx = Crear();
        var finalizado = NuevoDocumento(EstadoDocumento.Finalizado);
        var anulado = NuevoDocumento(EstadoDocumento.Anulado);
        var filtro = new FiltroDocumentos(null, 2026, null, null);
        ctx.Repo.Setup(r => r.ListarCerradosAsync(filtro))
            .ReturnsAsync(new List<DocumentoAdministrativo> { finalizado, anulado });

        var resultado = await ctx.Svc.ListarHistorialAsync(filtro);

        Assert.Equal(2, resultado.Count);
        ctx.Repo.Verify(r => r.ListarCerradosAsync(filtro), Times.Once);
        ctx.Repo.Verify(r => r.ListarActivosAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    // ── ObtenerPorIdAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerPorIdAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.ObtenerPorIdAsync(1));
    }

    [Fact]
    public async Task ObtenerPorIdAsync_DelegaAlRepo()
    {
        var ctx = Crear();
        var documento = NuevoDocumento();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        var resultado = await ctx.Svc.ObtenerPorIdAsync(1);

        Assert.Same(documento, resultado);
    }

    // ── IniciarProcesoAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task IniciarProcesoAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.IniciarProcesoAsync(1));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task IniciarProcesoAsync_DocumentoInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync((DocumentoAdministrativo?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => ctx.Svc.IniciarProcesoAsync(1));
    }

    [Fact]
    public async Task IniciarProcesoAsync_DesdePendiente_CambiaAEnProcesoYGeneraEventoAutomatico()
    {
        var ctx = Crear(idSesion: 3);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await ctx.Svc.IniciarProcesoAsync(1);

        Assert.Equal(EstadoDocumento.EnProceso, documento.Estado);
        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Equal(EstadoDocumento.Pendiente, evento.EstadoAnterior);
        Assert.Equal(EstadoDocumento.EnProceso, evento.EstadoNuevo);
        ctx.Repo.Verify(r => r.ActualizarAsync(documento), Times.Once);
    }

    [Fact]
    public async Task IniciarProcesoAsync_DesdeFinalizado_LanzaReglaDeNegocio()
    {
        // Guarda de estado origen (ver nota de Interfaces más abajo): Finalizado -> EnProceso
        // es una transición válida en la tabla del dominio (es la reapertura), así que sin
        // esta guarda explícita CambiarEstado no rechazaría esto y un Operador con solo
        // documentos.gestionar terminaría reabriendo un documento cerrado.
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Finalizado);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.IniciarProcesoAsync(1));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<DocumentoAdministrativo>()), Times.Never);
    }

    [Fact]
    public async Task IniciarProcesoAsync_DesdeAnulado_LanzaReglaDeNegocio()
    {
        // Mismo caso que el de arriba: Anulado -> EnProceso también es la reapertura.
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Anulado);
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.IniciarProcesoAsync(1));
    }

    [Fact]
    public async Task IniciarProcesoAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.IniciarProcesoAsync(5);

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", 5, It.IsAny<string>()), Times.Once);
    }

    // ── VolverAPendienteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task VolverAPendienteAsync_DesdeEnProceso_CambiaAPendienteYGeneraEventoAutomatico()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.VolverAPendienteAsync(5);

        Assert.Equal(EstadoDocumento.Pendiente, documento.Estado);
        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Equal(EstadoDocumento.EnProceso, evento.EstadoAnterior);
        Assert.Equal(EstadoDocumento.Pendiente, evento.EstadoNuevo);
    }

    [Fact]
    public async Task VolverAPendienteAsync_DesdePendiente_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.VolverAPendienteAsync(5));
    }

    [Fact]
    public async Task VolverAPendienteAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.VolverAPendienteAsync(5);

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", 5, It.IsAny<string>()), Times.Once);
    }

    // ── FinalizarAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task FinalizarAsync_DesdeEnProceso_CambiaAFinalizadoYSellaFechaCierre()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.FinalizarAsync(5);

        Assert.Equal(EstadoDocumento.Finalizado, documento.Estado);
        Assert.NotNull(documento.FechaCierre);
        Assert.True((DateTime.UtcNow - documento.FechaCierre!.Value) < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FinalizarAsync_GeneraEventoAutomaticoConEstados()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.FinalizarAsync(5);

        var evento = Assert.Single(documento.Eventos);
        Assert.True(evento.EsAutomatico);
        Assert.Equal(EstadoDocumento.EnProceso, evento.EstadoAnterior);
        Assert.Equal(EstadoDocumento.Finalizado, evento.EstadoNuevo);
    }

    [Fact]
    public async Task FinalizarAsync_DesdePendiente_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        var documento = NuevoDocumento(EstadoDocumento.Pendiente);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.FinalizarAsync(5));
    }

    [Fact]
    public async Task FinalizarAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        var documento = NuevoDocumento(EstadoDocumento.EnProceso);
        documento.Id = 5;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(documento);

        await ctx.Svc.FinalizarAsync(5);

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", 5, It.IsAny<string>()), Times.Once);
    }

    // ── AgregarNotaAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarNotaAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarDocumentos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.AgregarNotaAsync(1, "avance"));
    }

    [Fact]
    public async Task AgregarNotaAsync_TextoVacio_LanzaArgumentException()
    {
        var ctx = Crear();
        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.AgregarNotaAsync(1, "   "));
    }

    [Fact]
    public async Task AgregarNotaAsync_DocumentoInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync((DocumentoAdministrativo?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => ctx.Svc.AgregarNotaAsync(1, "avance"));
    }

    [Fact]
    public async Task AgregarNotaAsync_GuardaEventoManualYRegistraAuditoria()
    {
        var ctx = Crear(idSesion: 3);
        var documento = NuevoDocumento();
        documento.Id = 1;
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(documento);

        await ctx.Svc.AgregarNotaAsync(1, "avance del trámite");

        var evento = Assert.Single(documento.Eventos);
        Assert.False(evento.EsAutomatico);
        Assert.Null(evento.EstadoAnterior);
        Assert.Null(evento.EstadoNuevo);
        ctx.Repo.Verify(r => r.ActualizarAsync(documento), Times.Once);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            3, AccionAuditada.AltaNotaDocumento, "DocumentoAdministrativo", 1, It.IsAny<string>()), Times.Once);
    }
}
