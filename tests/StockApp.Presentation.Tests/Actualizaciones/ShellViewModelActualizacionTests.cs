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

        // El coordinador corre en background (fire-and-forget), damos un margen para que complete.
        await Task.Delay(200);

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
}
