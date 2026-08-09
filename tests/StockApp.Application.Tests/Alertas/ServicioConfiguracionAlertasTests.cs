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

        public void IniciarSesion(Usuario usuario) =>
            UsuarioActual = new UsuarioSesion(usuario.Id, usuario.NombreUsuario, usuario.Rol, null);

        public void CerrarSesion() => UsuarioActual = null;
    }

    private static (ServicioConfiguracionAlertas Sut, RepoFake Repo) Crear()
    {
        var repo = new RepoFake();
        var sut = new ServicioConfiguracionAlertas(
            repo, new AuthorizationService(), new SesionFake(), new NotificadorAlertasNulo());
        return (sut, repo);
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
}
