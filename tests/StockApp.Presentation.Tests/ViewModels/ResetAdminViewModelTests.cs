using Moq;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class ResetAdminViewModelTests
{
    [Fact]
    public async Task PedirDesafio_MuestraDesafioYCodigo()
    {
        var svc = new Mock<IResetAdminService>();
        svc.Setup(s => s.SolicitarDesafioAsync())
           .ReturnsAsync(new DesafioResetDto("nonce-1", "A3F2-9B41"));
        var vm = new ResetAdminViewModel(svc.Object);

        await vm.PedirDesafioCommand.ExecuteAsync(null);

        Assert.Equal("nonce-1", vm.Desafio);
        Assert.Equal("A3F2-9B41", vm.CodigoMaquina);
    }

    [Fact]
    public async Task Resetear_Exitoso_MarcaCompletadoYPermiteVolver()
    {
        var svc = new Mock<IResetAdminService>();
        svc.Setup(s => s.ResetearAsync("tok", "clave-nueva-9"))
           .ReturnsAsync(new ResultadoResetDto(true, null));
        var vm = new ResetAdminViewModel(svc.Object)
        {
            TokenPegado = "tok",
            NuevaContrasena = "clave-nueva-9",
        };

        await vm.ResetearCommand.ExecuteAsync(null);

        Assert.True(vm.Completado);
        Assert.Null(vm.MensajeError);
    }

    [Fact]
    public async Task Resetear_Fallido_MuestraMotivo()
    {
        var svc = new Mock<IResetAdminService>();
        svc.Setup(s => s.ResetearAsync("tok", "clave-nueva-9"))
           .ReturnsAsync(new ResultadoResetDto(false, "El desafío expiró. Pedí uno nuevo."));
        var vm = new ResetAdminViewModel(svc.Object)
        {
            TokenPegado = "tok",
            NuevaContrasena = "clave-nueva-9",
        };

        await vm.ResetearCommand.ExecuteAsync(null);

        Assert.False(vm.Completado);
        Assert.Equal("El desafío expiró. Pedí uno nuevo.", vm.MensajeError);
    }

    [Fact]
    public async Task PedirDesafio_RateLimit429_MuestraMensajeYNoPropagaExcepcion()
    {
        var svc = new Mock<IResetAdminService>();
        svc.Setup(s => s.SolicitarDesafioAsync())
           .ThrowsAsync(new ReglaDeNegocioException("Demasiados intentos, esperá un minuto y volvé a probar."));
        var vm = new ResetAdminViewModel(svc.Object);

        await vm.PedirDesafioCommand.ExecuteAsync(null);

        Assert.Equal("Demasiados intentos, esperá un minuto y volvé a probar.", vm.MensajeError);
    }

    [Fact]
    public async Task Resetear_RateLimit429_MuestraMensajeYNoPropagaExcepcion()
    {
        // Fix (Important): el 11º intento en un minuto lanza ReglaDeNegocioException
        // (mapeada desde el 429 en ApiErrores) — no debe escapar al handler global.
        var svc = new Mock<IResetAdminService>();
        svc.Setup(s => s.ResetearAsync("tok", "clave-nueva-9"))
           .ThrowsAsync(new ReglaDeNegocioException("Demasiados intentos, esperá un minuto y volvé a probar."));
        var vm = new ResetAdminViewModel(svc.Object)
        {
            TokenPegado = "tok",
            NuevaContrasena = "clave-nueva-9",
        };

        await vm.ResetearCommand.ExecuteAsync(null);

        Assert.False(vm.Completado);
        Assert.Equal("Demasiados intentos, esperá un minuto y volvé a probar.", vm.MensajeError);
    }

    [Fact]
    public void Volver_DisparaElEvento()
    {
        var vm = new ResetAdminViewModel(Mock.Of<IResetAdminService>());
        var volvio = false;
        vm.Volver += () => volvio = true;

        vm.VolverCommand.Execute(null);

        Assert.True(volvio);
    }
}
