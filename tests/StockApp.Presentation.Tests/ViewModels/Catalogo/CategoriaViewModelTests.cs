using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class CategoriaListViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (CategoriaListViewModel vm, Mock<ICategoriaService> svcMock, Mock<INavigationService> navMock)
        Crear(IReadOnlyList<Categoria>? categorias = null)
    {
        var svcMock = new Mock<ICategoriaService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(categorias ?? new List<Categoria>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new CategoriaListViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    // ── D4.1 tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var categorias = new List<Categoria>
        {
            new() { Id = 1, Nombre = "Electrónica" },
            new() { Id = 2, Nombre = "Ferretería" }
        };
        var (vm, svcMock, _) = Crear(categorias);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Electrónica", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task NuevoCommand_NavegaACategoriaFormViewModel()
    {
        var (vm, _, navMock) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<CategoriaFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_LlamaServicio()
    {
        var cat = new Categoria { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _) = Crear(new List<Categoria> { cat });
        await vm.CargarAsync();
        vm.ItemSeleccionado = cat;

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }
}

public class CategoriaFormViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (CategoriaFormViewModel vm, Mock<ICategoriaService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<ICategoriaService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<Categoria>()))
            .ReturnsAsync(1);

        var navMock = new Mock<INavigationService>();
        var vm = new CategoriaFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Electrónica";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<Categoria>(c => c.Nombre == "Electrónica")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Electrónica";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<CategoriaListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }
}
