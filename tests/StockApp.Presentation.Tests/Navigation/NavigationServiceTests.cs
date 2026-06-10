using Moq;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.Navigation;

public class NavigationServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// VM mínimo de test — solo necesita ser ViewModelBase.
    /// </summary>
    private class FakeViewModelA : ViewModelBase { }
    private class FakeViewModelB : ViewModelBase { }

    private static INavigationService CrearConResolver(Func<Type, object> resolver)
        => new NavigationService(resolver);

    // ── D1.1 tests ───────────────────────────────────────────────────────────

    [Fact]
    public void Navegar_PublicaViewModelCorrecto()
    {
        var vmA = new FakeViewModelA();
        var svc = CrearConResolver(t => vmA);

        svc.Navegar<FakeViewModelA>();

        Assert.Same(vmA, svc.Actual);
    }

    [Fact]
    public void Navegar_InvocaResolverConTipoVM()
    {
        Type? tipoResuelto = null;
        var vm = new FakeViewModelA();
        var svc = CrearConResolver(t =>
        {
            tipoResuelto = t;
            return vm;
        });

        svc.Navegar<FakeViewModelA>();

        Assert.Equal(typeof(FakeViewModelA), tipoResuelto);
    }

    [Fact]
    public void CambiadoEvent_Dispara_AlNavegar()
    {
        var vm = new FakeViewModelA();
        var svc = CrearConResolver(_ => vm);
        var disparado = false;
        svc.Cambiado += () => disparado = true;

        svc.Navegar<FakeViewModelA>();

        Assert.True(disparado);
    }

    [Fact]
    public void Navegar_DobleVezMismoTipo_NoFalla()
    {
        var vm = new FakeViewModelA();
        var svc = CrearConResolver(_ => vm);

        // No debe lanzar excepción en la segunda navegación
        svc.Navegar<FakeViewModelA>();
        svc.Navegar<FakeViewModelA>();

        Assert.Same(vm, svc.Actual);
    }

    [Fact]
    public void Navegar_DosVMsDistintos_PublicaElUltimo()
    {
        var vmA = new FakeViewModelA();
        var vmB = new FakeViewModelB();
        var svc = CrearConResolver(t =>
            t == typeof(FakeViewModelA) ? (object)vmA : vmB);

        svc.Navegar<FakeViewModelA>();
        svc.Navegar<FakeViewModelB>();

        Assert.Same(vmB, svc.Actual);
    }
}
