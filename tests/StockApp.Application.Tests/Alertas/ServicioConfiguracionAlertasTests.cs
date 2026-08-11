using StockApp.Application.Alertas;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Alertas;

public class ServicioConfiguracionAlertasTests
{
    private sealed class RepoFake : IConfiguracionAlertasRepository
    {
        public ConfiguracionAlertas Configuracion { get; set; } = new();
        public ConfiguracionAlertas? Guardada { get; private set; }

        public Task<ConfiguracionAlertas> ObtenerAsync() => Task.FromResult(Configuracion);

        public Task GuardarAsync(ConfiguracionAlertas configuracion)
        {
            Guardada = configuracion;
            Configuracion = configuracion;
            return Task.CompletedTask;
        }
    }

    // ICurrentSession real tiene 5 miembros y UsuarioActual es UsuarioSesion?, no Usuario?
    // (el brief original asumía una forma que no compila -- ver la interfaz real).
    private sealed class SesionFake : ICurrentSession
    {
        public bool EstaAutenticado => UsuarioActual is not null;
        public UsuarioSesion? UsuarioActual { get; set; } = new UsuarioSesion(3, "admin", RolUsuario.Admin, "Admin");
        public RolUsuario? RolActual => UsuarioActual?.Rol;
        public IReadOnlySet<string> PermisosActuales => new HashSet<string>();
        public void EstablecerPermisos(IReadOnlySet<string> permisos) { }

        public void IniciarSesion(Usuario usuario) =>
            UsuarioActual = new UsuarioSesion(usuario.Id, usuario.NombreUsuario, usuario.Rol, null);

        public void CerrarSesion() => UsuarioActual = null;
    }

    /// <summary>
    /// Fake que REGISTRA la URL pingueada y devuelve un resultado configurable. Es lo que permite
    /// afirmar que ProbarAsync devuelve el resultado REAL del notificador (fix crítico del review
    /// final) y que prueba la URL de pantalla y no la guardada (fix I2).
    /// </summary>
    private sealed class NotificadorFake : INotificadorAlertas
    {
        private readonly ResultadoPruebaAlertaDto _resultado;

        public NotificadorFake(ResultadoPruebaAlertaDto? resultado = null) =>
            _resultado = resultado ?? new ResultadoPruebaAlertaDto(true, 200, "ok");

        public List<string> UrlsPingueadas { get; } = new();

        public Task NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ResultadoPruebaAlertaDto> ProbarPingAsync(string url, CancellationToken ct = default)
        {
            UrlsPingueadas.Add(url);
            return Task.FromResult(_resultado);
        }
    }

    private static (ServicioConfiguracionAlertas Sut, RepoFake Repo) Crear()
    {
        var repo = new RepoFake();
        var sut = new ServicioConfiguracionAlertas(
            repo, new AuthorizationService(), new SesionFake(), new NotificadorAlertasNulo());
        return (sut, repo);
    }

    private static (ServicioConfiguracionAlertas Sut, RepoFake Repo, NotificadorFake Notificador) CrearConNotificador(
        ResultadoPruebaAlertaDto? resultado = null)
    {
        var repo = new RepoFake();
        var notificador = new NotificadorFake(resultado);
        var sut = new ServicioConfiguracionAlertas(
            repo, new AuthorizationService(), new SesionFake(), notificador);
        return (sut, repo, notificador);
    }

    [Fact]
    public async Task GuardarAsync_UrlHttp_RechazaConArgumentException()
    {
        var (sut, _) = Crear();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.GuardarAsync("http://hc-ping.com/abc", habilitado: true));

        Assert.Contains("https", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuardarAsync_UrlRelativa_RechazaConArgumentException()
    {
        var (sut, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.GuardarAsync("hc-ping.com/abc", habilitado: true));
    }

    [Fact]
    public async Task GuardarAsync_HabilitadoSinUrl_RechazaConArgumentException()
    {
        var (sut, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.GuardarAsync(null, habilitado: true));
    }

    [Fact]
    public async Task GuardarAsync_DeshabilitadoSinUrl_EsValido()
    {
        var (sut, repo) = Crear();

        await sut.GuardarAsync(null, habilitado: false);

        Assert.NotNull(repo.Guardada);
        Assert.False(repo.Guardada!.Habilitado);
        Assert.Null(repo.Guardada.UrlWebhook);
    }

    [Fact]
    public async Task GuardarAsync_UrlValida_PersisteYSellaAutorYFecha()
    {
        var (sut, repo) = Crear();

        await sut.GuardarAsync("https://hc-ping.com/abc", habilitado: true);

        Assert.Equal("https://hc-ping.com/abc", repo.Guardada!.UrlWebhook);
        Assert.True(repo.Guardada.Habilitado);
        Assert.Equal(3, repo.Guardada.ActualizadoPorUsuarioId);
        Assert.NotEqual(default, repo.Guardada.ActualizadoEn);
    }

    [Fact]
    public async Task ObtenerAsync_DevuelveElEstadoActual()
    {
        var (sut, repo) = Crear();
        repo.Configuracion = new ConfiguracionAlertas
        {
            UrlWebhook = "https://hc-ping.com/xyz",
            Habilitado = true,
            ActualizadoEn = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc),
        };

        var dto = await sut.ObtenerAsync();

        Assert.Equal("https://hc-ping.com/xyz", dto.UrlWebhook);
        Assert.True(dto.Habilitado);
    }

    // ── ProbarAsync ───────────────────────────────────────────────────────────
    //
    // Fix CRÍTICO del review final: ProbarAsync devolvía (true, null, "Se envió un ping de
    // prueba.") INCONDICIONALMENTE, porque estaba construido sobre NotificarCorridaBackupAsync,
    // que no devuelve nada. El verificador del canal no podía fallar nunca.

    [Fact]
    public async Task ProbarAsync_ElWebhookRechazaElPing_DevuelveElFalloYElStatusCodeReal()
    {
        var (sut, repo, _) = CrearConNotificador(new ResultadoPruebaAlertaDto(false, 404, "El webhook respondió 404."));
        repo.Configuracion = new ConfiguracionAlertas { UrlWebhook = "https://hc-ping.com/typo", Habilitado = true };

        var resultado = await sut.ProbarAsync();

        Assert.False(resultado.Exitoso);
        Assert.Equal(404, resultado.StatusCode);
    }

    [Fact]
    public async Task ProbarAsync_ElWebhookAcepta_DevuelveElStatusCodeReal()
    {
        var (sut, repo, _) = CrearConNotificador(new ResultadoPruebaAlertaDto(true, 200, "El webhook respondió 200."));
        repo.Configuracion = new ConfiguracionAlertas { UrlWebhook = "https://hc-ping.com/abc", Habilitado = true };

        var resultado = await sut.ProbarAsync();

        Assert.True(resultado.Exitoso);
        Assert.Equal(200, resultado.StatusCode);
    }

    [Fact]
    public async Task ProbarAsync_SinUrlEnPantalla_PingueaLaGuardada()
    {
        var (sut, repo, notificador) = CrearConNotificador();
        repo.Configuracion = new ConfiguracionAlertas { UrlWebhook = "https://hc-ping.com/guardada", Habilitado = true };

        await sut.ProbarAsync();

        Assert.Equal("https://hc-ping.com/guardada", Assert.Single(notificador.UrlsPingueadas));
    }

    [Fact]
    public async Task ProbarAsync_ConUrlEnPantalla_PingueaEsaYNoLaGuardada()
    {
        // Fix IMPORTANTE (I2): el flujo natural es pegar la URL y apretar Probar. Antes eso
        // pingueaba la URL VIEJA (o decía "no hay URL configurada").
        var (sut, repo, notificador) = CrearConNotificador();
        repo.Configuracion = new ConfiguracionAlertas { UrlWebhook = "https://hc-ping.com/vieja", Habilitado = true };

        await sut.ProbarAsync("https://hc-ping.com/nueva");

        Assert.Equal("https://hc-ping.com/nueva", Assert.Single(notificador.UrlsPingueadas));
    }

    [Fact]
    public async Task ProbarAsync_ConUrlEnPantalla_NoLaPersiste()
    {
        var (sut, repo, _) = CrearConNotificador();
        repo.Configuracion = new ConfiguracionAlertas { UrlWebhook = "https://hc-ping.com/vieja", Habilitado = true };

        await sut.ProbarAsync("https://hc-ping.com/nueva");

        Assert.Null(repo.Guardada);
        Assert.Equal("https://hc-ping.com/vieja", repo.Configuracion.UrlWebhook);
    }

    [Fact]
    public async Task ProbarAsync_UrlEnPantallaHttp_RechazaConArgumentException()
    {
        // La validación no es negociable: es la MISMA superficie SSRF que GuardarAsync. "Probar
        // sin guardar" no puede ser la puerta de atrás que saltea el gate de https.
        var (sut, _, notificador) = CrearConNotificador();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ProbarAsync("http://hc-ping.com/abc"));

        Assert.Contains("https", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(notificador.UrlsPingueadas);
    }

    [Fact]
    public async Task ProbarAsync_UrlEnPantallaRelativa_RechazaConArgumentException()
    {
        var (sut, _, notificador) = CrearConNotificador();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.ProbarAsync("hc-ping.com/abc"));

        Assert.Empty(notificador.UrlsPingueadas);
    }

    [Fact]
    public async Task ProbarAsync_ConUrlEnPantallaYCanalDeshabilitado_IgualPrueba()
    {
        // Probar ANTES de habilitar es el orden real en que se configura esto por primera vez.
        var (sut, repo, notificador) = CrearConNotificador();
        repo.Configuracion = new ConfiguracionAlertas { UrlWebhook = null, Habilitado = false };

        var resultado = await sut.ProbarAsync("https://hc-ping.com/nueva");

        Assert.True(resultado.Exitoso);
        Assert.Single(notificador.UrlsPingueadas);
    }

    [Fact]
    public async Task ProbarAsync_SinUrlEnPantallaNiGuardada_NoPingueaYAvisa()
    {
        var (sut, repo, notificador) = CrearConNotificador();
        repo.Configuracion = new ConfiguracionAlertas { UrlWebhook = null, Habilitado = false };

        var resultado = await sut.ProbarAsync();

        Assert.False(resultado.Exitoso);
        Assert.Empty(notificador.UrlsPingueadas);
    }

    [Fact]
    public async Task ProbarAsync_SinUrlEnPantallaYCanalGuardadoDeshabilitado_NoPinguea()
    {
        var (sut, repo, notificador) = CrearConNotificador();
        repo.Configuracion = new ConfiguracionAlertas { UrlWebhook = "https://hc-ping.com/abc", Habilitado = false };

        var resultado = await sut.ProbarAsync();

        Assert.False(resultado.Exitoso);
        Assert.Empty(notificador.UrlsPingueadas);
    }
}
