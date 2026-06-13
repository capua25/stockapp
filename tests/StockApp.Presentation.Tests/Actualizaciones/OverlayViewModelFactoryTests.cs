using StockApp.Application.Actualizaciones;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.Actualizaciones;

/// <summary>
/// Verifica el mapeo ModoUx → ViewModel de overlay (los 5 casos).
/// Rojo primero: CoordinadorActualizacion.ResolverOverlayViewModel no existe aún.
/// </summary>
public class OverlayViewModelFactoryTests
{
    private static AccionUx Accion(ModoUx modo) =>
        new(modo, "texto", Posponible: modo == ModoUx.BannerDiscreto || modo == ModoUx.ModalPosponible, ReintentaEnArranque: false);

    [Fact]
    public void Ninguno_DevuelveNull()
    {
        var accion = Accion(ModoUx.Ninguno);
        var resultado = CoordinadorActualizacion.ResolverOverlayViewModel(accion);
        Assert.Null(resultado);
    }

    [Fact]
    public void BannerDiscreto_DevuelveActualizacionBannerViewModel()
    {
        var accion = Accion(ModoUx.BannerDiscreto);
        var resultado = CoordinadorActualizacion.ResolverOverlayViewModel(accion);
        Assert.IsType<ActualizacionBannerViewModel>(resultado);
    }

    [Fact]
    public void ModalPosponible_DevuelveActualizacionModalViewModel()
    {
        var accion = Accion(ModoUx.ModalPosponible);
        var resultado = CoordinadorActualizacion.ResolverOverlayViewModel(accion);
        Assert.IsType<ActualizacionModalViewModel>(resultado);
    }

    [Fact]
    public void BloqueoCritico_DevuelveActualizacionBloqueoViewModel()
    {
        var accion = Accion(ModoUx.BloqueoCritico);
        var resultado = CoordinadorActualizacion.ResolverOverlayViewModel(accion);
        Assert.IsType<ActualizacionBloqueoViewModel>(resultado);
    }

    [Fact]
    public void ModoDegradado_DevuelveActualizacionBloqueoViewModel()
    {
        var accion = Accion(ModoUx.ModoDegradado);
        var resultado = CoordinadorActualizacion.ResolverOverlayViewModel(accion);
        Assert.IsType<ActualizacionBloqueoViewModel>(resultado);
    }
}
