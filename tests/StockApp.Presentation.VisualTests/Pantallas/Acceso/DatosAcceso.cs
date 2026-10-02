using Moq;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auth;
using StockApp.Application.Licenciamiento;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.VisualTests.Pantallas.Acceso;

/// <summary>Datos fijos de docs/ui-v2/mockups/acceso/*.html.</summary>
public static class DatosAcceso
{
    public const string CodigoMaquina = "7F3A-91C2-4D8E-B605";
    public const string Desafio = "K4PZ-8QWN-2M7R";

    private static IInfoApp Info() => Mock.Of<IInfoApp>(i => i.Version == "0.4.0");

    public static LoginViewModel Login(bool soloAccesoLimitado = false, string? usuario = null, string? error = null)
    {
        var vm = new LoginViewModel(Mock.Of<IAuthService>(), Shell(), Info(), soloAccesoLimitado);
        if (usuario is not null)
        {
            vm.NombreUsuario = usuario;
        }

        if (error is not null)
        {
            vm.MensajeError = error;
        }

        return vm;
    }

    public static ResetAdminViewModel Reset()
    {
        var vm = new ResetAdminViewModel(Mock.Of<IResetAdminService>());
        vm.CodigoMaquina = CodigoMaquina;
        vm.Desafio = Desafio;
        return vm;
    }

    // El código lo carga el DataContextChanged de BloqueoLicenciaView (CargarEstadoAsync): si la
    // vista perdiera ese wiring, el mockup no coincide.
    public static BloqueoLicenciaViewModel BloqueoLicencia()
    {
        var licencia = new Mock<ILicenciaService>();
        licencia.Setup(l => l.ObtenerEstadoAsync()).ReturnsAsync(new EstadoLicenciaDto(false, CodigoMaquina));
        return new BloqueoLicenciaViewModel(licencia.Object);
    }

    // LoginViewModel exige un ShellViewModel; ninguno de sus servicios se usa al renderizar.
    private static ShellViewModel Shell() =>
        new(Mock.Of<IAuthService>(), Mock.Of<ILicenciaService>(), Mock.Of<IResetAdminService>(), Mock.Of<INavigationService>(),
            new CoordinadorActualizacion(Mock.Of<IUpdateService>(), new PoliticaUxActualizacion()), Mock.Of<IUiDispatcher>(), Info());
}
