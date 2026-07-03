using StockApp.Application.Actualizaciones;
using StockApp.Presentation.Actualizaciones;
using Xunit;

namespace StockApp.Presentation.Tests.Actualizaciones;

public class ActualizacionBannerViewModelTests
{
    [Fact]
    public void TextoMarkdown_Refleja_TextoDeAccionUx()
    {
        var accion = new AccionUx(ModoUx.BannerDiscreto, "severity: normal\n\nNueva versión.", Posponible: true, ReintentaEnArranque: true);
        var vm = new ActualizacionBannerViewModel(accion);

        Assert.Equal("severity: normal\n\nNueva versión.", vm.TextoMarkdown);
    }

    [Fact]
    public void EsPosponible_EsTrue_ParaBannerDiscreto()
    {
        var accion = new AccionUx(ModoUx.BannerDiscreto, null, Posponible: true, ReintentaEnArranque: true);
        var vm = new ActualizacionBannerViewModel(accion);

        Assert.True(vm.EsPosponible);
    }

    [Fact]
    public void Titulo_IncluyeLaVersion_CuandoAccionUxLaTrae()
    {
        var accion = new AccionUx(ModoUx.BannerDiscreto, null, Posponible: true, ReintentaEnArranque: true, Version: "0.1.2");
        var vm = new ActualizacionBannerViewModel(accion);

        Assert.Equal("Nueva versión v0.1.2 disponible", vm.Titulo);
    }

    [Fact]
    public void Titulo_UsaTextoGenerico_CuandoNoHayVersion()
    {
        var accion = new AccionUx(ModoUx.BannerDiscreto, null, Posponible: true, ReintentaEnArranque: true);
        var vm = new ActualizacionBannerViewModel(accion);

        Assert.Equal("Nueva versión disponible", vm.Titulo);
    }

    [Fact]
    public void PosponerCommand_DisparaPosponerSolicitado()
    {
        var accion = new AccionUx(ModoUx.BannerDiscreto, null, Posponible: true, ReintentaEnArranque: true);
        var vm = new ActualizacionBannerViewModel(accion);

        var disparado = false;
        vm.PosponerSolicitado += () => disparado = true;

        vm.PosponerCommand.Execute(null);

        Assert.True(disparado);
    }
}

public class ActualizacionModalViewModelTests
{
    [Fact]
    public void TextoMarkdown_QuedaLimpio_SinLaLineaDeSeverity()
    {
        var accion = new AccionUx(ModoUx.ModalPosponible, "severity: important\n\nActualización importante.", Posponible: true, ReintentaEnArranque: true);
        var vm = new ActualizacionModalViewModel(accion);

        Assert.Equal("Actualización importante.", vm.TextoMarkdown);
    }

    [Fact]
    public void EsPosponible_EsTrue_ParaModalPosponible()
    {
        var accion = new AccionUx(ModoUx.ModalPosponible, null, Posponible: true, ReintentaEnArranque: true);
        var vm = new ActualizacionModalViewModel(accion);

        Assert.True(vm.EsPosponible);
    }
}

public class ActualizacionBloqueoViewModelTests
{
    [Fact]
    public void TextoMarkdown_QuedaLimpio_SinLaLineaDeSeverity()
    {
        var accion = new AccionUx(ModoUx.BloqueoCritico, "severity: critical\n\nActualización urgente.", Posponible: false, ReintentaEnArranque: true);
        var vm = new ActualizacionBloqueoViewModel(accion);

        Assert.Equal("Actualización urgente.", vm.TextoMarkdown);
    }

    [Fact]
    public void EsPosponible_EsFalse_ParaBloqueoCritico()
    {
        var accion = new AccionUx(ModoUx.BloqueoCritico, null, Posponible: false, ReintentaEnArranque: true);
        var vm = new ActualizacionBloqueoViewModel(accion);

        Assert.False(vm.EsPosponible);
    }

    [Fact]
    public void EsModoDegradado_EsFalse_CuandoModoEsBloqueoCritico()
    {
        var accion = new AccionUx(ModoUx.BloqueoCritico, null, Posponible: false, ReintentaEnArranque: true);
        var vm = new ActualizacionBloqueoViewModel(accion);

        Assert.False(vm.EsModoDegradado);
    }

    [Fact]
    public void EsModoDegradado_EsTrue_CuandoModoEsModoDegradado()
    {
        var accion = new AccionUx(ModoUx.ModoDegradado, "sin red", Posponible: false, ReintentaEnArranque: true);
        var vm = new ActualizacionBloqueoViewModel(accion);

        Assert.True(vm.EsModoDegradado);
    }
}
