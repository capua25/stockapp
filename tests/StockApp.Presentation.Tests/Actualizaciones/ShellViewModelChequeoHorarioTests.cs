using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.Actualizaciones;

public class ShellViewModelChequeoHorarioTests
{
    private static readonly IInfoApp InfoAppStub = Mock.Of<IInfoApp>(x => x.Version == "0.0.0");
    private static readonly TimeSpan CotaEspera = TimeSpan.FromSeconds(10);

    /// <summary>Dispatcher inline; puede fallar en el próximo Post para simular un tick que explota.</summary>
    private sealed class FakeUiDispatcher : IUiDispatcher
    {
        public bool FallarEnElProximoPost;

        public void Post(Action accion)
        {
            if (FallarEnElProximoPost)
            {
                FallarEnElProximoPost = false;
                throw new InvalidOperationException("Post falló");
            }
            accion();
        }
    }

    /// <summary>
    /// Espera controlada por el test: cada EsperarAsync registra el intervalo y se completa cuando
    /// lo acumulado por Avanzar desde ese pedido alcanza el intervalo (o se cancela el token).
    /// Sin Task.Delay ni relojes reales.
    /// </summary>
    private sealed class EsperaManual : IEsperaChequeo
    {
        private readonly object _lock = new();
        private TaskCompletionSource? _actual;
        private TimeSpan _restante;
        private int _senalesSinConsumir;
        private readonly Queue<TaskCompletionSource> _consumidores = new();

        public List<TimeSpan> Pedidos { get; } = new();

        public Task EsperarAsync(TimeSpan intervalo, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            lock (_lock)
            {
                Pedidos.Add(intervalo);
                _actual   = tcs;
                _restante = intervalo;
                if (_consumidores.Count > 0) _consumidores.Dequeue().TrySetResult();
                else _senalesSinConsumir++;
            }
            return tcs.Task;
        }

        /// <summary>Se completa cuando el bucle (vuelve a) llamar a EsperarAsync. Acotada: falla por aserción, no cuelga.</summary>
        public Task SiguienteEsperaAsync()
        {
            TaskCompletionSource tcs;
            lock (_lock)
            {
                if (_senalesSinConsumir > 0)
                {
                    _senalesSinConsumir--;
                    return Task.CompletedTask;
                }
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _consumidores.Enqueue(tcs);
            }
            return tcs.Task.WaitAsync(CotaEspera);
        }

        public void Avanzar(TimeSpan tiempo)
        {
            lock (_lock)
            {
                if (_actual is null) return;
                _restante -= tiempo;
                if (_restante <= TimeSpan.Zero) _actual.TrySetResult();
            }
        }
    }

    private static UpdateCheckResult Hay(string version, UpdateSeverity sev)
        => new(HayUpdate: true, Version: version, Severity: sev, NotasMarkdown: "notas");

    private static (ShellViewModel Vm, Mock<IUpdateService> Update, EsperaManual Espera, FakeUiDispatcher Ui)
        Crear(UpdateCheckResult arranque)
    {
        var update = new Mock<IUpdateService>();
        update.Setup(s => s.BuscarAsync(default)).ReturnsAsync(arranque);
        var espera = new EsperaManual();
        var ui = new FakeUiDispatcher();
        var vm = new ShellViewModel(
            Mock.Of<IAuthService>(),
            Mock.Of<ILicenciaService>(x => x.ObtenerEstadoAsync() == Task.FromResult(new EstadoLicenciaDto(true, "MAQ"))),
            Mock.Of<IResetAdminService>(),
            new NavigationService(t => throw new InvalidOperationException(t.Name)),
            new CoordinadorActualizacion(update.Object, new PoliticaUxActualizacion()),
            ui,
            InfoAppStub,
            esperaChequeo: espera);
        return (vm, update, espera, ui);
    }

    private static async Task ArrancarAsync(ShellViewModel vm, EsperaManual espera)
    {
        await vm.InicializarAsync();
        await vm._tareaActualizacion;
        await espera.SiguienteEsperaAsync();
    }

    [Fact]
    public async Task PrimerTick_PideUnaHora()
    {
        var (vm, _, espera, _) = Crear(UpdateCheckResult.SinUpdate);
        await ArrancarAsync(vm, espera);

        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(espera.Pedidos));
        vm.Dispose();
    }

    [Fact]
    public async Task Avanzar59Minutos_NoBusca()
    {
        var (vm, update, espera, _) = Crear(UpdateCheckResult.SinUpdate);
        await ArrancarAsync(vm, espera);

        espera.Avanzar(TimeSpan.FromMinutes(59));

        update.Verify(s => s.BuscarAsync(default), Times.Once);
        vm.Dispose();
    }

    [Fact]
    public async Task Avanzar60Minutos_BuscaUnaVez()
    {
        var (vm, update, espera, _) = Crear(UpdateCheckResult.SinUpdate);
        await ArrancarAsync(vm, espera);

        espera.Avanzar(TimeSpan.FromMinutes(59));
        espera.Avanzar(TimeSpan.FromMinutes(1));
        await espera.SiguienteEsperaAsync();

        update.Verify(s => s.BuscarAsync(default), Times.Exactly(2));
        vm.Dispose();
    }

    [Fact]
    public async Task Dispose_DetieneElBucle()
    {
        var (vm, update, espera, _) = Crear(UpdateCheckResult.SinUpdate);
        await ArrancarAsync(vm, espera);

        vm.Dispose();

        await vm._tareaChequeoHorario.WaitAsync(CotaEspera);
        espera.Avanzar(TimeSpan.FromHours(1));
        update.Verify(s => s.BuscarAsync(default), Times.Once);
    }

    [Fact]
    public async Task TickConFalla_ElSiguienteTickVuelveABuscar()
    {
        var (vm, update, espera, _) = Crear(UpdateCheckResult.SinUpdate);
        await ArrancarAsync(vm, espera);
        update.Setup(s => s.BuscarAsync(default)).ThrowsAsync(new HttpRequestException("sin red"));

        espera.Avanzar(TimeSpan.FromHours(1));
        await espera.SiguienteEsperaAsync();
        espera.Avanzar(TimeSpan.FromHours(1));
        await espera.SiguienteEsperaAsync();

        update.Verify(s => s.BuscarAsync(default), Times.Exactly(3));
        vm.Dispose();
    }

    [Fact]
    public async Task TickQueExplotaFueraDelCoordinador_NoMataLosSiguientes()
    {
        var (vm, update, espera, ui) = Crear(UpdateCheckResult.SinUpdate);
        await ArrancarAsync(vm, espera);

        // Tick 1: hay update pero el Post al UI thread explota (excepción que el coordinador no atrapa).
        update.Setup(s => s.BuscarAsync(default)).ReturnsAsync(Hay("0.6.0", UpdateSeverity.Normal));
        ui.FallarEnElProximoPost = true;
        espera.Avanzar(TimeSpan.FromHours(1));
        await espera.SiguienteEsperaAsync();
        Assert.Null(vm.OverlayActualizacion);

        // Tick 2: otra versión; el bucle sigue vivo y la muestra.
        update.Setup(s => s.BuscarAsync(default)).ReturnsAsync(Hay("0.7.0", UpdateSeverity.Normal));
        espera.Avanzar(TimeSpan.FromHours(1));
        await espera.SiguienteEsperaAsync();

        var banner = Assert.IsType<ActualizacionBannerViewModel>(vm.OverlayActualizacion);
        Assert.Equal("0.7.0", banner.Version);
        vm.Dispose();
    }

    [Fact]
    public async Task TickCritico_AsignaElAvisoCritico_YActualizarAhoraAplica()
    {
        var (vm, update, espera, _) = Crear(UpdateCheckResult.SinUpdate);
        await ArrancarAsync(vm, espera);
        update.Setup(s => s.BuscarAsync(default)).ReturnsAsync(Hay("0.6.0", UpdateSeverity.Critical));

        espera.Avanzar(TimeSpan.FromHours(1));
        await espera.SiguienteEsperaAsync();

        var aviso = Assert.IsType<ActualizacionAvisoCriticoViewModel>(vm.OverlayActualizacion);
        aviso.ActualizarAhoraCommand.Execute(null);
        await vm._tareaAplicarActualizacion.WaitAsync(CotaEspera);

        update.Verify(s => s.DescargarAsync(It.IsAny<IProgress<UpdateProgress>?>(), It.IsAny<CancellationToken>()), Times.Once);
        update.Verify(s => s.AplicarYReiniciar(), Times.Once);
        vm.Dispose();
    }

    [Fact]
    public async Task TickConBannerNuevo_ConModalAbierto_NoLoReemplaza()
    {
        var (vm, update, espera, _) = Crear(Hay("0.5.0", UpdateSeverity.Important));
        await ArrancarAsync(vm, espera);
        var modal = Assert.IsType<ActualizacionModalViewModel>(vm.OverlayActualizacion);
        update.Setup(s => s.BuscarAsync(default)).ReturnsAsync(Hay("0.6.0", UpdateSeverity.Normal));

        espera.Avanzar(TimeSpan.FromHours(1));
        await espera.SiguienteEsperaAsync();

        Assert.Same(modal, vm.OverlayActualizacion);
        vm.Dispose();
    }

    private static ViewModelBase? Overlay(string clave) => clave switch
    {
        "null"      => null,
        "banner"    => CoordinadorActualizacion.ResolverOverlayViewModel(new(ModoUx.BannerDiscreto, null, true, true)),
        "modal"     => CoordinadorActualizacion.ResolverOverlayViewModel(new(ModoUx.ModalPosponible, null, true, true)),
        "bloqueo"   => CoordinadorActualizacion.ResolverOverlayViewModel(new(ModoUx.BloqueoCritico, null, false, false)),
        "degradado" => CoordinadorActualizacion.ResolverOverlayViewModel(new(ModoUx.ModoDegradado, null, false, true)),
        "aviso"     => CoordinadorActualizacion.ResolverOverlayViewModel(new(ModoUx.AvisoCriticoEnSesion, null, false, true)),
        _           => throw new ArgumentException(clave),
    };

    [Theory]
    [InlineData("null",      ModoUx.BannerDiscreto,        true)]
    [InlineData("null",      ModoUx.AvisoCriticoEnSesion,  true)]
    [InlineData("banner",    ModoUx.BannerDiscreto,        true)]
    [InlineData("banner",    ModoUx.AvisoCriticoEnSesion,  true)]
    [InlineData("modal",     ModoUx.BannerDiscreto,        false)]
    [InlineData("modal",     ModoUx.AvisoCriticoEnSesion,  true)]
    [InlineData("bloqueo",   ModoUx.BannerDiscreto,        false)]
    [InlineData("bloqueo",   ModoUx.AvisoCriticoEnSesion,  false)]
    [InlineData("degradado", ModoUx.BannerDiscreto,        false)]
    [InlineData("degradado", ModoUx.AvisoCriticoEnSesion,  false)]
    [InlineData("aviso",     ModoUx.BannerDiscreto,        false)]
    [InlineData("aviso",     ModoUx.AvisoCriticoEnSesion,  true)]
    public void DebeReemplazarOverlay_Reglas(string actual, ModoUx nuevo, bool esperado)
        => Assert.Equal(esperado, ShellViewModel.DebeReemplazarOverlay(Overlay(actual), nuevo));
}
