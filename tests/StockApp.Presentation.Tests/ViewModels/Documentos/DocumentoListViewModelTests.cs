using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
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
              RolUsuario rol = RolUsuario.Operador)
    {
        var svc = new Mock<IDocumentoAdministrativoService>();
        svc.Setup(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ReturnsAsync(activos ?? new List<DocumentoAdministrativo>());
        svc.Setup(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()))
            .ReturnsAsync(historial ?? new List<DocumentoAdministrativo>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);

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
    public async Task AbrirHistorialCommand_PrimeraVez_CargaElHistorialConElAnioActual()
    {
        var ctx = Crear(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(3, EstadoDocumento.Finalizado),
        });
        Assert.Equal(DateTime.UtcNow.Year, ctx.Vm.FiltroHistorialAnio);

        await ctx.Vm.AbrirHistorialCommand.ExecuteAsync(null);

        Assert.Single(ctx.Vm.Historial);
        ctx.Svc.Verify(s => s.ListarHistorialAsync(
            It.Is<FiltroDocumentos>(f => f.Anio == DateTime.UtcNow.Year)), Times.Once);
    }

    [Fact]
    public async Task AbrirHistorialCommand_SegundaVez_NoVuelveALlamarAlServicio()
    {
        var ctx = Crear();

        await ctx.Vm.AbrirHistorialCommand.ExecuteAsync(null);
        await ctx.Vm.AbrirHistorialCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Once);
    }

    [Fact]
    public async Task CargarHistorialAsync_LlamadaDirecta_SiempreVuelveAConsultar()
    {
        // A diferencia de AbrirHistorialCommand (una sola vez), CargarHistorialAsync() es el
        // método que dispara el botón "Buscar" del filtro del historial: debe poder recargar
        // cuantas veces el usuario cambie el filtro.
        var ctx = Crear();

        await ctx.Vm.CargarHistorialAsync();
        await ctx.Vm.CargarHistorialAsync();

        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Exactly(2));
    }

    [Fact]
    public async Task BuscarHistorialCommand_RecargaLaColeccionHistorial()
    {
        // F2: el botón "Buscar" del filtro del historial (Task 22) bindea este comando --
        // CargarHistorialAsync() es un método plano, no un ICommand, así que hace falta un
        // [RelayCommand] propio que lo envuelva.
        var ctx = Crear(historial: new List<DocumentoAdministrativo>
        {
            DocumentoDe(3, EstadoDocumento.Finalizado),
        });

        await ctx.Vm.BuscarHistorialCommand.ExecuteAsync(null);

        Assert.Single(ctx.Vm.Historial);
        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Once);
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
    public async Task FinalizarCommand_InvalidaElCacheDelHistorial_LaProximaAperturaVuelveAConsultar()
    {
        // M9: _historialCargado quedaba en true después de la primera apertura de la solapa
        // Historial; finalizar un documento desde Activos no lo invalidaba, así que el
        // documento recién cerrado no aparecía hasta apretar "Buscar" a mano.
        var ctx = Crear(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        });
        await ctx.Vm.CargarAsync();
        await ctx.Vm.AbrirHistorialCommand.ExecuteAsync(null);
        var fila = ctx.Vm.Activos[0];

        await ctx.Vm.FinalizarCommand.ExecuteAsync(fila);
        await ctx.Vm.AbrirHistorialCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ListarHistorialAsync(It.IsAny<FiltroDocumentos>()), Times.Exactly(2));
    }

    [Fact]
    public async Task BuscarActivosCommand_RecargaLaColeccionActivos()
    {
        // I2: FiltroActivosTexto/Tipo no tenían ningún disparador propio -- el único punto de
        // recarga era DataContextChanged, que corre una sola vez al entrar a la pantalla.
        var ctx = Crear(activos: new List<DocumentoAdministrativo>
        {
            DocumentoDe(1, EstadoDocumento.Pendiente),
        });

        await ctx.Vm.BuscarActivosCommand.ExecuteAsync(null);

        Assert.Single(ctx.Vm.Activos);
        ctx.Svc.Verify(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>()), Times.Once);
    }

    [Fact]
    public async Task BuscarActivosCommand_ConFiltroTipoElegido_LoEnviaAlServicio()
    {
        var ctx = Crear();
        ctx.Vm.FiltroActivosTipo = TipoDocumento.Oficio;

        await ctx.Vm.BuscarActivosCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ListarActivosAsync(
            It.Is<FiltroDocumentos>(f => f.Tipo == TipoDocumento.Oficio)), Times.Once);
    }

    [Fact]
    public async Task BuscarHistorialCommand_ConFiltroTipoElegido_LoEnviaAlServicio()
    {
        var ctx = Crear();
        ctx.Vm.FiltroHistorialTipo = TipoDocumento.Suministro;

        await ctx.Vm.BuscarHistorialCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ListarHistorialAsync(
            It.Is<FiltroDocumentos>(f => f.Tipo == TipoDocumento.Suministro)), Times.Once);
    }

    [Fact]
    public async Task BuscarHistorialCommand_ConFiltroEstadoElegido_LoEnviaAlServicio()
    {
        var ctx = Crear();
        ctx.Vm.FiltroHistorialEstado = EstadoDocumento.Anulado;

        await ctx.Vm.BuscarHistorialCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ListarHistorialAsync(
            It.Is<FiltroDocumentos>(f => f.Estado == EstadoDocumento.Anulado)), Times.Once);
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
    public async Task CargarAsync_ServidorNoDisponible_InformaAlUsuario()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ThrowsAsync(new ServidorNoDisponibleException());

        await ctx.Vm.CargarAsync();

        ctx.Confirm.Verify(c => c.InformarAsync(ServidorNoDisponibleException.MensajePorDefecto), Times.Once);
    }
}
