using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Documentos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Documentos;

public class DocumentoListViewModelTests
{
    private static DocumentoAdministrativo DocumentoDe(int id, EstadoDocumento estado) => new()
    {
        Id = id, Numero = "0087", Anio = 2026, Tipo = TipoDocumento.Expediente,
        FechaEmision = DateTime.UtcNow.Date, Descripcion = "Bacheo calle Rivera",
        Estado = estado, RegistradoPorUsuarioId = 1, FechaRegistro = DateTime.UtcNow,
    };

    private static (DocumentoListViewModel Vm, Mock<IDocumentoAdministrativoService> Svc, Mock<IConfirmacionService> Confirm)
        Crear(IReadOnlyList<DocumentoAdministrativo>? activos = null,
              IReadOnlyList<DocumentoAdministrativo>? historial = null,
              RolUsuario rol = RolUsuario.Operador,
              HashSet<string>? permisos = null)
    {
        var svc = new Mock<IDocumentoAdministrativoService>();
        svc.Setup(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ReturnsAsync(activos ?? new List<DocumentoAdministrativo>());
        svc.Setup(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()))
            .ReturnsAsync(historial ?? new List<DocumentoAdministrativo>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.PermisosActuales).Returns(permisos ?? new HashSet<string>());

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new DocumentoListViewModel(svc.Object, session.Object, nav.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    [Fact]
    public async Task CargarAsync_ListaDocumentosActivos_LosAgregaALaColeccion()
    {
        var ctx = Crear(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
            DocumentoDe(2, EstadoDocumento.EnProceso),
        });

        await ctx.Vm.CargarAsync();

        Assert.Equal(2, ctx.Vm.Activos.Count);
        Assert.Empty(ctx.Vm.Historial);
    }

    [Fact]
    public async Task CargarAsync_NoDisparaLaCargaDelHistorial()
    {
        // D9: la carga del historial es perezosa -- CargarAsync() inicial NO debe pedirlo.
        var ctx = Crear();

        await ctx.Vm.CargarAsync();

        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    [Fact]
    public async Task ElegirHistorial_ConsultaElHistorial()
    {
        var ctx = Crear(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(3, EstadoDocumento.Finalizado),
        });
        ctx.Vm.EsperaDebounce = TimeSpan.Zero;
        Assert.Equal(DateTime.UtcNow.Year, ctx.Vm.FiltroHistorialAnio);
        await ctx.Vm.CargarAsync();

        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;

        Assert.Single(ctx.Vm.Historial);
        ctx.Svc.Verify(s => s.ListarHistorialAsync(
            It.Is<FiltroDocumentos>(f => f.Anio == DateTime.UtcNow.Year)), Times.Once);
    }

    [Fact]
    public async Task ElegirHistorialDosVeces_ConsultaCadaVez()
    {
        // Cambia el contrato de la solapa: cada vez que se elige la vista se consulta (siempre se ve lo último).
        var ctx = Crear();
        ctx.Vm.EsperaDebounce = TimeSpan.Zero;
        await ctx.Vm.CargarAsync();

        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;
        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[0];
        await ctx.Vm._tareaDebounce;
        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;

        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CargarHistorialAsync_LlamadaDirecta_SiempreVuelveAConsultar()
    {
        // CargarHistorialAsync() recarga el historial cuantas veces el usuario cambie un filtro.
        var ctx = Crear();

        await ctx.Vm.CargarHistorialAsync();
        await ctx.Vm.CargarHistorialAsync();

        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CambiarFiltroEnHistorial_RecargaLaColeccionHistorial()
    {
        var ctx = Crear(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(3, EstadoDocumento.Finalizado),
        });
        ctx.Vm.EsperaDebounce = TimeSpan.Zero;
        await ctx.Vm.CargarAsync();
        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;
        Assert.Single(ctx.Vm.Historial);

        ctx.Vm.FiltroHistorialTexto = "bacheo";
        await ctx.Vm._tareaDebounce;

        Assert.Single(ctx.Vm.Historial);
        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CargarAsync_ConRolOperador_FilaNoPuedeAnularNiReabrir_AunqueLaTransicionSeaValida()
    {
        // Importante 5 del spec: el dominio permite Pendiente->Anulado (PuedeTransicionarA da
        // true), pero AnularAsync es documentos.administrar -- un Operador no debe ver el botón.
        var ctx = Crear(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        }, rol: RolUsuario.Operador);

        await ctx.Vm.CargarAsync();

        var fila = ctx.Vm.Activos[0];
        Assert.True(fila.Documento.PuedeTransicionarA(EstadoDocumento.Anulado));
        Assert.False(fila.PuedeAnular);
        Assert.False(fila.PuedeReabrir);
        Assert.True(fila.PuedeIniciar);
    }

    [Fact]
    public async Task CargarAsync_DocumentoFinalizado_FilaNoPuedeIniciar_AunqueLaTransicionSeaValida()
    {
        // C6/Hallazgo Task 8: Finalizado -> EnProceso es una transición válida en el dominio (es
        // la reapertura), así que PuedeTransicionarA(EnProceso) solo no alcanza para gatear el
        // botón "Iniciar" -- si alcanzara, un Operador con documentos.gestionar vería "Iniciar"
        // habilitado sobre un documento cerrado y comería el 409 que el servicio ya rechaza.
        var ctx = Crear(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Finalizado),
        }, rol: RolUsuario.Admin);

        await ctx.Vm.CargarAsync();
        await ctx.Vm.CargarHistorialAsync();

        var fila = ctx.Vm.Historial[0];
        Assert.True(fila.Documento.PuedeTransicionarA(EstadoDocumento.EnProceso));
        Assert.False(fila.PuedeIniciar);
    }

    [Fact]
    public async Task CargarAsync_ConRolAdmin_FilaPuedeAnular()
    {
        var ctx = Crear(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        }, rol: RolUsuario.Admin);

        await ctx.Vm.CargarAsync();

        Assert.True(ctx.Vm.Activos[0].PuedeAnular);
    }

    [Fact]
    public async Task IniciarAsync_LlamaAlServicioYRecarga()
    {
        var ctx = Crear(activos: new List<DocumentoAdministrativo> { DocumentoDe(1, EstadoDocumento.Pendiente) });
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Activos[0];

        await ctx.Vm.IniciarCommand.ExecuteAsync(fila);

        ctx.Svc.Verify(s => s.IniciarProcesoAsync(1), Times.Once);
    }

    [Fact]
    public async Task Finalizar_YElegirHistorial_ElCerradoAparece()
    {
        // Antes el historial se cacheaba tras abrirlo y había que invalidarlo al finalizar; ahora cada elección consulta.
        var ctx = Crear(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        });
        ctx.Vm.EsperaDebounce = TimeSpan.Zero;
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Activos[0];
        await ctx.Vm.FinalizarCommand.ExecuteAsync(fila);
        ctx.Svc.Setup(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()))
            .ReturnsAsync(new List<DocumentoAdministrativo> { DocumentoDe(1, EstadoDocumento.Finalizado) });

        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;

        Assert.Single(ctx.Vm.Historial);
        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Once);
    }

    [Fact]
    public async Task CambiarTipoEnActivos_RecargaLaColeccionActivos()
    {
        var ctx = Crear(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        });
        ctx.Vm.EsperaDebounce = TimeSpan.Zero;
        await ctx.Vm.CargarAsync();

        ctx.Vm.FiltroActivosTipoSeleccionado = ctx.Vm.TiposDisponibles[1];
        await ctx.Vm._tareaDebounce;

        Assert.Single(ctx.Vm.Activos);
        ctx.Svc.Verify(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CambiarTipoEnActivos_LoEnviaAlServicio()
    {
        var ctx = Crear();
        ctx.Vm.EsperaDebounce = TimeSpan.Zero;
        await ctx.Vm.CargarAsync();

        ctx.Vm.FiltroActivosTipo = TipoDocumento.Oficio;
        await ctx.Vm._tareaDebounce;

        ctx.Svc.Verify(s => s.ListarActivosAsync(
            It.Is<FiltroDocumentos>(f => f.Tipo == TipoDocumento.Oficio)), Times.Once);
    }

    [Fact]
    public async Task CambiarFiltroEnHistorial_Tipo_LoEnviaAlServicio()
    {
        var ctx = Crear();
        ctx.Vm.EsperaDebounce = TimeSpan.Zero;
        await ctx.Vm.CargarAsync();
        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;

        ctx.Vm.FiltroHistorialTipo = TipoDocumento.Suministro;
        await ctx.Vm._tareaDebounce;

        ctx.Svc.Verify(s => s.ListarHistorialAsync(
            It.Is<FiltroDocumentos>(f => f.Tipo == TipoDocumento.Suministro)), Times.Once);
    }

    [Fact]
    public async Task CambiarFiltroEnHistorial_Estado_LoEnviaAlServicio()
    {
        var ctx = Crear();
        ctx.Vm.EsperaDebounce = TimeSpan.Zero;
        await ctx.Vm.CargarAsync();
        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;

        ctx.Vm.FiltroHistorialEstado = EstadoDocumento.Anulado;
        await ctx.Vm._tareaDebounce;

        ctx.Svc.Verify(s => s.ListarHistorialAsync(
            It.Is<FiltroDocumentos>(f => f.Estado == EstadoDocumento.Anulado)), Times.Once);
    }

    [Fact]
    public async Task CantidadEnVista_YHayFiltrosEnLaVista_SiguenALaVistaActual()
    {
        var ctx = Crear(activos: new List<DocumentoAdministrativo> { DocumentoDe(1, EstadoDocumento.Pendiente), DocumentoDe(2, EstadoDocumento.EnProceso) });
        ctx.Vm.EsperaDebounce = TimeSpan.Zero;
        await ctx.Vm.CargarAsync();
        Assert.Equal(2, ctx.Vm.CantidadEnVista);
        Assert.False(ctx.Vm.HayFiltrosEnLaVista);

        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;

        Assert.Equal(0, ctx.Vm.CantidadEnVista);
        Assert.True(ctx.Vm.HayFiltrosEnLaVista);
    }

    [Fact]
    public void TiposDisponibles_EmpiezaConLaOpcionTodosQueMapeaANull()
    {
        var ctx = Crear();

        Assert.Equal("Todos", ctx.Vm.TiposDisponibles[0].Nombre);
        Assert.Null(ctx.Vm.TiposDisponibles[0].Valor);
    }

    [Fact]
    public void EstadosDisponibles_EmpiezaConLaOpcionTodosQueMapeaANull()
    {
        var ctx = Crear();

        Assert.Equal("Todos", ctx.Vm.EstadosDisponibles[0].Nombre);
        Assert.Null(ctx.Vm.EstadosDisponibles[0].Valor);
    }

    [Fact]
    public void FiltroActivosTipoSeleccionado_AlCambiarlo_ActualizaFiltroActivosTipo()
    {
        var ctx = Crear();
        var oficio = ctx.Vm.TiposDisponibles.Single(o => o.Valor == TipoDocumento.Oficio);

        ctx.Vm.FiltroActivosTipoSeleccionado = oficio;

        Assert.Equal(TipoDocumento.Oficio, ctx.Vm.FiltroActivosTipo);
    }

    [Fact]
    public void FiltroHistorialEstadoSeleccionado_AlCambiarlo_ActualizaFiltroHistorialEstado()
    {
        var ctx = Crear();
        var anulado = ctx.Vm.EstadosDisponibles.Single(o => o.Valor == EstadoDocumento.Anulado);

        ctx.Vm.FiltroHistorialEstadoSeleccionado = anulado;

        Assert.Equal(EstadoDocumento.Anulado, ctx.Vm.FiltroHistorialEstado);
    }

    [Fact]
    public async Task CargarAsync_SesionSinPermiso_NoInformaAlUsuario()
    {
        // El manejador central del 403 (AuthTokenHandler + AccesoRevocado) ya avisa; si el
        // módulo también informara, vuelve el doble aviso corregido en el commit 093fc7c.
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.CargarAsync();

        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IniciarAsync_ServidorNoDisponible_InformaAlUsuario()
    {
        // La carga ya no informa por modal (queda SinConexion en línea); las acciones sobre la fila sí.
        var ctx = Crear(activos: new List<DocumentoAdministrativo> { DocumentoDe(1, EstadoDocumento.Pendiente) });
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Activos[0];
        ctx.Svc.Setup(s => s.IniciarProcesoAsync(1)).ThrowsAsync(new ServidorNoDisponibleException());

        await ctx.Vm.IniciarCommand.ExecuteAsync(fila);

        ctx.Confirm.Verify(c => c.InformarAsync(ServidorNoDisponibleException.MensajePorDefecto), Times.Once);
    }

    // ── Vista Activos/Historial con recarga automática, carga protegida y revocación ──

    private static DocumentoListViewModel SinEspera(DocumentoListViewModel vm)
    {
        vm.EsperaDebounce = TimeSpan.Zero;
        return vm;
    }

    private static int LlamadasAActivos(Mock<IDocumentoAdministrativoService> svc)
        => svc.Invocations.Count(i => i.Method.Name == nameof(IDocumentoAdministrativoService.ListarActivosAsync));

    private static int LlamadasAlHistorial(Mock<IDocumentoAdministrativoService> svc)
        => svc.Invocations.Count(i => i.Method.Name == nameof(IDocumentoAdministrativoService.ListarHistorialAsync));

    [Fact]
    public void DocumentoFila_NumeroCompleto_EsNumeroBarraAnio()
    {
        var fila = new DocumentoFila(DocumentoDe(1, EstadoDocumento.Pendiente), RolUsuario.Operador);

        Assert.Equal("0087/2026", fila.NumeroCompleto);
    }

    [Fact]
    public async Task CargarAsync_PorDefecto_ConsultaActivosYNoElHistorial()
    {
        var ctx = Crear(activos: new List<DocumentoAdministrativo> { DocumentoDe(1, EstadoDocumento.Pendiente) });

        await ctx.Vm.CargarAsync();

        Assert.Equal(1, LlamadasAActivos(ctx.Svc));
        Assert.Equal(0, LlamadasAlHistorial(ctx.Svc));
        Assert.False(ctx.Vm.EsVistaHistorial);
        Assert.Equal(new[] { "Activos", "Historial" }, ctx.Vm.Vistas.Select(v => v.Nombre));
    }

    [Fact]
    public async Task CambiarAHistorial_ConsultaElHistorialConElAnioActual()
    {
        var ctx = Crear(historial: new List<DocumentoAdministrativo> { DocumentoDe(3, EstadoDocumento.Finalizado) });
        SinEspera(ctx.Vm);
        await ctx.Vm.CargarAsync();

        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;

        Assert.True(ctx.Vm.EsVistaHistorial);
        Assert.Single(ctx.Vm.Historial);
        ctx.Svc.Verify(s => s.ListarHistorialAsync(
            It.Is<FiltroDocumentos>(f => f.Anio == DateTime.UtcNow.Year)), Times.Once);
    }

    [Fact]
    public async Task VolverAActivos_VuelveAConsultarActivos()
    {
        var ctx = Crear();
        SinEspera(ctx.Vm);
        await ctx.Vm.CargarAsync();
        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;

        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[0];
        await ctx.Vm._tareaDebounce;

        Assert.Equal(2, LlamadasAActivos(ctx.Svc));
        Assert.Equal(1, LlamadasAlHistorial(ctx.Svc));
    }

    [Fact]
    public async Task CambiarTipoYTextoEnActivos_UnaSolaConsulta()
    {
        var ctx = Crear();
        ctx.Vm.EsperaDebounce = TimeSpan.FromMilliseconds(50);
        await ctx.Vm.CargarAsync();

        ctx.Vm.FiltroActivosTipoSeleccionado = ctx.Vm.TiposDisponibles[2];
        ctx.Vm.FiltroActivosTexto = "bacheo";
        await ctx.Vm._tareaDebounce;

        Assert.Equal(2, LlamadasAActivos(ctx.Svc));
        ctx.Svc.Verify(s => s.ListarActivosAsync(
            It.Is<FiltroDocumentos>(f => f.Tipo == TipoDocumento.Oficio && f.Texto == "bacheo")), Times.Once);
    }

    [Fact]
    public async Task AnioVacioOInvalido_NoConsultaElHistorial()
    {
        var ctx = Crear();
        SinEspera(ctx.Vm);
        await ctx.Vm.CargarAsync();
        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        await ctx.Vm._tareaDebounce;
        var antes = LlamadasAlHistorial(ctx.Svc);

        ctx.Vm.FiltroHistorialAnio = null;
        await ctx.Vm._tareaDebounce;
        Assert.Equal(antes, LlamadasAlHistorial(ctx.Svc));

        ctx.Vm.HayErroresDeEntrada = true;
        ctx.Vm.FiltroHistorialAnio = 2024;
        await ctx.Vm._tareaDebounce;
        Assert.Equal(antes, LlamadasAlHistorial(ctx.Svc));
    }

    [Fact]
    public async Task FiltrosPuestosAntesDeLaCargaInicial_NoProgramanConsulta()
    {
        var ctx = Crear();
        SinEspera(ctx.Vm);

        ctx.Vm.FiltroActivosTipoSeleccionado = ctx.Vm.TiposDisponibles[1];
        ctx.Vm.FiltroActivosTexto = "x";
        await ctx.Vm._tareaDebounce;
        Assert.Equal(0, LlamadasAActivos(ctx.Svc));

        await ctx.Vm.CargarAsync();
        Assert.Equal(1, LlamadasAActivos(ctx.Svc));
    }

    [Fact]
    public async Task DosConsultasEnOrdenInverso_LaViejaNoPisaLaNueva()
    {
        var ctx = Crear();
        var primera = new TaskCompletionSource<IReadOnlyList<DocumentoAdministrativo>>();
        var segunda = new TaskCompletionSource<IReadOnlyList<DocumentoAdministrativo>>();
        ctx.Svc.SetupSequence(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()))
            .Returns(primera.Task).Returns(segunda.Task);
        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];

        var vieja = ctx.Vm.CargarAsync();
        var nueva = ctx.Vm.CargarAsync();
        segunda.SetResult(new List<DocumentoAdministrativo> { DocumentoDe(9, EstadoDocumento.Finalizado) });
        await nueva;
        primera.SetResult(new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Finalizado), DocumentoDe(2, EstadoDocumento.Anulado), DocumentoDe(3, EstadoDocumento.Anulado),
        });
        await vieja;

        Assert.Single(ctx.Vm.Historial);
        Assert.Equal(9, ctx.Vm.Historial[0].Id);
    }

    [Fact]
    public async Task CargarAsync_Un403_SinPermisoConElMensaje()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>())).ThrowsAsync(new UnauthorizedAccessException());

        await ctx.Vm.CargarAsync();

        Assert.True(ctx.Vm.SinPermiso);
        Assert.Equal(DocumentoListViewModel.MensajeSinPermisoDocumentos, ctx.Vm.MensajeSinPermiso);
        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CargarAsync_ServidorCaido_SinConexionYVaciaLaVistaActual()
    {
        var ctx = Crear(activos: new List<DocumentoAdministrativo> { DocumentoDe(1, EstadoDocumento.Pendiente) });
        await ctx.Vm.CargarAsync();
        Assert.Single(ctx.Vm.Activos);
        ctx.Svc.Setup(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>())).ThrowsAsync(new ServidorNoDisponibleException());

        await ctx.Vm.CargarAsync();

        Assert.True(ctx.Vm.SinConexion);
        Assert.Empty(ctx.Vm.Activos);
        ctx.Confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Reintentar_ConsultaConLosFiltrosDePantalla()
    {
        var ctx = Crear();
        ctx.Vm.EsperaDebounce = TimeSpan.FromHours(1);
        ctx.Svc.SetupSequence(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(new List<DocumentoAdministrativo> { DocumentoDe(1, EstadoDocumento.Pendiente) });
        await ctx.Vm.CargarAsync();
        Assert.True(ctx.Vm.SinConexion);

        ctx.Vm.FiltroActivosTipoSeleccionado = ctx.Vm.TiposDisponibles[2];
        await ctx.Vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(ctx.Vm.SinConexion);
        Assert.Single(ctx.Vm.Activos);
        ctx.Svc.Verify(s => s.ListarActivosAsync(It.Is<FiltroDocumentos>(f => f.Tipo == TipoDocumento.Oficio)), Times.Once);
    }

    [Fact]
    public async Task FilaSeleccionada_SigueALaVistaActual()
    {
        var ctx = Crear();
        SinEspera(ctx.Vm);
        await ctx.Vm.CargarAsync();
        var activa = new DocumentoFila(DocumentoDe(1, EstadoDocumento.Pendiente), RolUsuario.Operador);
        var cerrada = new DocumentoFila(DocumentoDe(2, EstadoDocumento.Finalizado), RolUsuario.Operador);
        var cambios = new List<string?>();
        ctx.Vm.PropertyChanged += (_, e) => cambios.Add(e.PropertyName);

        ctx.Vm.FilaSeleccionadaActivos = activa;
        ctx.Vm.FilaSeleccionadaHistorial = cerrada;
        Assert.Same(activa, ctx.Vm.FilaSeleccionada);

        ctx.Vm.VistaSeleccionada = ctx.Vm.Vistas[1];
        Assert.Same(cerrada, ctx.Vm.FilaSeleccionada);
        Assert.Contains(nameof(DocumentoListViewModel.FilaSeleccionada), cambios);
    }

    [Fact]
    public void HayFiltrosEnActivos_ConTipoOTexto()
    {
        var ctx = Crear();
        Assert.False(ctx.Vm.HayFiltrosEnActivos);

        ctx.Vm.FiltroActivosTipoSeleccionado = ctx.Vm.TiposDisponibles[1];
        Assert.True(ctx.Vm.HayFiltrosEnActivos);

        ctx.Vm.FiltroActivosTipoSeleccionado = ctx.Vm.TiposDisponibles[0];
        Assert.False(ctx.Vm.HayFiltrosEnActivos);

        ctx.Vm.FiltroActivosTexto = "oficio";
        Assert.True(ctx.Vm.HayFiltrosEnActivos);
    }

    [Fact]
    public async Task NotificarPermisosActualizados_SinGestionarDocumentos_VaciaYMarcaSinPermiso()
    {
        var permisos = new HashSet<string> { Permisos.GestionarDocumentos };
        var ctx = Crear(activos: new List<DocumentoAdministrativo> { DocumentoDe(1, EstadoDocumento.Pendiente) }, permisos: permisos);
        await ctx.Vm.CargarAsync();
        Assert.Single(ctx.Vm.Activos);

        permisos.Remove(Permisos.GestionarDocumentos);
        ctx.Vm.NotificarPermisosActualizados();

        Assert.Empty(ctx.Vm.Activos);
        Assert.True(ctx.Vm.SinPermiso);
        Assert.Equal(DocumentoListViewModel.MensajeSinPermisoDocumentos, ctx.Vm.MensajeSinPermiso);
    }
}
