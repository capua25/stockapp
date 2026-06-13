using Moq;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.Actualizaciones;

public class ShellViewModelActualizacionTests
{
    /// <summary>
    /// Verifica que InicializarAsync dispara EvaluarEnArranqueAsync del coordinador.
    /// Usa un Mock de IUpdateService que responde SinUpdate para que el coordinador
    /// complete sin errores — nos importa que lo llame, no el resultado.
    /// </summary>
    [Fact]
    public async Task InicializarAsync_DispararCoordinadorEnBackground_SinBloquearArranque()
    {
        // Arrange
        var updateMock = new Mock<IUpdateService>();
        updateMock
            .Setup(s => s.BuscarAsync(default))
            .ReturnsAsync(UpdateCheckResult.SinUpdate);

        var coordinador = new CoordinadorActualizacion(
            updateMock.Object,
            new PoliticaUxActualizacion());

        var primerArranqueMock = new Mock<IPrimerArranqueService>();
        primerArranqueMock
            .Setup(p => p.RequiereCrearAdminAsync())
            .ReturnsAsync(false);

        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var navSvc = new NavigationService(t =>
        {
            if (t == typeof(ShellMainViewModel))
                return new ShellMainViewModel(sessionMock.Object, Mock.Of<INavigationService>());
            throw new InvalidOperationException($"Tipo no registrado en test: {t.Name}");
        });

        var shell = new ShellViewModel(
            primerArranqueMock.Object,
            Mock.Of<IAuthService>(),
            Mock.Of<IUsuarioService>(),
            navSvc,
            coordinador);

        // Act
        await shell.InicializarAsync();

        // Awaitamos la tarea de background directamente (determinista).
        await shell._tareaActualizacion;

        // Assert: el coordinador evaluó (BuscarAsync fue llamado exactamente una vez).
        updateMock.Verify(s => s.BuscarAsync(default), Times.Once);
    }

    [Fact]
    public async Task InicializarAsync_CoordinadorFalla_NoTumbaElArranque()
    {
        // Arrange: el update service lanza excepción
        var updateMock = new Mock<IUpdateService>();
        updateMock
            .Setup(s => s.BuscarAsync(default))
            .ThrowsAsync(new Exception("Error de red"));

        var coordinador = new CoordinadorActualizacion(
            updateMock.Object,
            new PoliticaUxActualizacion());

        var primerArranqueMock = new Mock<IPrimerArranqueService>();
        primerArranqueMock
            .Setup(p => p.RequiereCrearAdminAsync())
            .ReturnsAsync(false);

        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var navSvc = new NavigationService(t =>
        {
            if (t == typeof(ShellMainViewModel))
                return new ShellMainViewModel(sessionMock.Object, Mock.Of<INavigationService>());
            throw new InvalidOperationException($"Tipo no registrado en test: {t.Name}");
        });

        var shell = new ShellViewModel(
            primerArranqueMock.Object,
            Mock.Of<IAuthService>(),
            Mock.Of<IUsuarioService>(),
            navSvc,
            coordinador);

        // Act + Assert: no lanza excepción
        var exception = await Record.ExceptionAsync(() => shell.InicializarAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task InicializarAsync_BannerDiscreto_OverlayActualizacionEsBannerViewModel()
    {
        // Arrange: update service devuelve BannerDiscreto
        var updateMock = new Mock<IUpdateService>();
        updateMock
            .Setup(s => s.BuscarAsync(default))
            .ReturnsAsync(new UpdateCheckResult(
                HayUpdate: true,
                Version: "1.1.0",
                Severity: UpdateSeverity.Normal,
                NotasMarkdown: "nueva versión"));

        var coordinador = new CoordinadorActualizacion(
            updateMock.Object,
            new PoliticaUxActualizacion());

        var primerArranqueMock = new Mock<IPrimerArranqueService>();
        primerArranqueMock
            .Setup(p => p.RequiereCrearAdminAsync())
            .ReturnsAsync(false);

        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var navSvc = new NavigationService(t =>
        {
            if (t == typeof(ShellMainViewModel))
                return new ShellMainViewModel(sessionMock.Object, Mock.Of<INavigationService>());
            throw new InvalidOperationException($"Tipo no registrado en test: {t.Name}");
        });

        var shell = new ShellViewModel(
            primerArranqueMock.Object,
            Mock.Of<IAuthService>(),
            Mock.Of<IUsuarioService>(),
            navSvc,
            coordinador);

        // Act
        await shell.InicializarAsync();

        // Awaitamos la tarea de background directamente (determinista, sin Task.Delay).
        await shell._tareaActualizacion;

        // Assert: el overlay debe ser un BannerViewModel
        Assert.IsType<ActualizacionBannerViewModel>(shell.OverlayActualizacion);
    }

    [Fact]
    public async Task InicializarAsync_SinUpdate_OverlayActualizacionEsNull()
    {
        // Arrange: no hay update → overlay debe quedar null
        var updateMock = new Mock<IUpdateService>();
        updateMock
            .Setup(s => s.BuscarAsync(default))
            .ReturnsAsync(UpdateCheckResult.SinUpdate);

        var coordinador = new CoordinadorActualizacion(
            updateMock.Object,
            new PoliticaUxActualizacion());

        var primerArranqueMock = new Mock<IPrimerArranqueService>();
        primerArranqueMock
            .Setup(p => p.RequiereCrearAdminAsync())
            .ReturnsAsync(false);

        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var navSvc = new NavigationService(t =>
        {
            if (t == typeof(ShellMainViewModel))
                return new ShellMainViewModel(sessionMock.Object, Mock.Of<INavigationService>());
            throw new InvalidOperationException($"Tipo no registrado en test: {t.Name}");
        });

        var shell = new ShellViewModel(
            primerArranqueMock.Object,
            Mock.Of<IAuthService>(),
            Mock.Of<IUsuarioService>(),
            navSvc,
            coordinador);

        // Act
        await shell.InicializarAsync();
        await shell._tareaActualizacion;

        // Assert
        Assert.Null(shell.OverlayActualizacion);
    }
}
