using Moq;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class BloqueoLicenciaViewModelTests
{
    [Fact]
    public async Task CargarEstado_MuestraElCodigoDeMaquina()
    {
        var svc = new Mock<ILicenciaService>();
        svc.Setup(s => s.ObtenerEstadoAsync())
           .ReturnsAsync(new EstadoLicenciaDto(false, "A3F2-9B41"));
        var vm = new BloqueoLicenciaViewModel(svc.Object);

        await vm.CargarEstadoAsync();

        Assert.Equal("A3F2-9B41", vm.CodigoMaquina);
    }

    [Fact]
    public async Task Activar_Exitosa_DisparaLicenciaActivada()
    {
        var svc = new Mock<ILicenciaService>();
        svc.Setup(s => s.ActivarAsync("lic")).ReturnsAsync(new ResultadoActivacionDto(true, null));
        var vm = new BloqueoLicenciaViewModel(svc.Object) { LicenciaPegada = "lic" };
        var activada = false;
        vm.LicenciaActivada += () => activada = true;

        await vm.ActivarCommand.ExecuteAsync(null);

        Assert.True(activada);
        Assert.Null(vm.MensajeError);
    }

    [Fact]
    public async Task Activar_Fallida_MuestraMotivoYNoDispara()
    {
        var svc = new Mock<ILicenciaService>();
        svc.Setup(s => s.ActivarAsync("lic"))
           .ReturnsAsync(new ResultadoActivacionDto(false, "La licencia fue emitida para otra máquina."));
        var vm = new BloqueoLicenciaViewModel(svc.Object) { LicenciaPegada = "lic" };
        var activada = false;
        vm.LicenciaActivada += () => activada = true;

        await vm.ActivarCommand.ExecuteAsync(null);

        Assert.False(activada);
        Assert.Equal("La licencia fue emitida para otra máquina.", vm.MensajeError);
    }

    [Fact]
    public async Task Activar_RateLimit429_MuestraMensajeYNoPropagaExcepcion()
    {
        // Fix (Important): el 11º intento en un minuto lanza ReglaDeNegocioException
        // (mapeada desde el 429 en ApiErrores) — no debe escapar al handler global.
        var svc = new Mock<ILicenciaService>();
        svc.Setup(s => s.ActivarAsync("lic"))
           .ThrowsAsync(new ReglaDeNegocioException("Demasiados intentos, esperá un minuto y volvé a probar."));
        var vm = new BloqueoLicenciaViewModel(svc.Object) { LicenciaPegada = "lic" };
        var activada = false;
        vm.LicenciaActivada += () => activada = true;

        await vm.ActivarCommand.ExecuteAsync(null);

        Assert.False(activada);
        Assert.Equal("Demasiados intentos, esperá un minuto y volvé a probar.", vm.MensajeError);
    }

    [Fact]
    public void Activar_DeshabilitadoSinLicenciaPegada()
    {
        var vm = new BloqueoLicenciaViewModel(Mock.Of<ILicenciaService>());

        Assert.False(vm.ActivarCommand.CanExecute(null));
        vm.LicenciaPegada = "algo";
        Assert.True(vm.ActivarCommand.CanExecute(null));
    }

    [Fact]
    public void IrALoginAccesoLimitado_DisparaIngresoLimitadoSolicitado()
    {
        // FIX 1 (IMPORTANT, re-review final E1): único camino a los backups con licencia
        // vencida. El Shell lo cablea a MostrarLoginAccesoLimitado.
        var vm = new BloqueoLicenciaViewModel(Mock.Of<ILicenciaService>());
        var disparado = false;
        vm.IngresoLimitadoSolicitado += () => disparado = true;

        vm.IrALoginAccesoLimitadoCommand.Execute(null);

        Assert.True(disparado);
    }
}
