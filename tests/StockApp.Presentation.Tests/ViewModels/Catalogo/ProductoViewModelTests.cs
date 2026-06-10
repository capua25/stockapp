using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class ProductoListViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ProductoListViewModel vm, Mock<IProductoService> svcMock, Mock<INavigationService> navMock)
        Crear(IReadOnlyList<Producto>? productos = null)
    {
        var svcMock = new Mock<IProductoService>();
        svcMock
            .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(productos ?? new List<Producto>());

        var navMock = new Mock<INavigationService>();
        var vm = new ProductoListViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    // ── D3.1 tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CargarAsync_LlamaServicioYPopulaItems()
    {
        var productos = new List<Producto>
        {
            new() { Id = 1, Codigo = "P001", Nombre = "Producto Uno" },
            new() { Id = 2, Codigo = "P002", Nombre = "Producto Dos" }
        };
        var (vm, svcMock, _) = Crear(productos);

        await vm.CargarAsync();

        svcMock.Verify(s => s.BuscarAsync(null, null, null), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("P001", vm.Items[0].Codigo);
    }

    [Fact]
    public async Task Buscar_FiltroNombre_InvocaBuscarAsyncConNombre()
    {
        var (vm, svcMock, _) = Crear();
        vm.FiltroBusqueda = "aceite";

        await vm.BuscarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BuscarAsync(null, null, "aceite"), Times.Once);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAProductoFormViewModel()
    {
        var (vm, _, navMock) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ProductoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task CargarAsync_SinProductos_ItemsVacio()
    {
        var (vm, _, _) = Crear(new List<Producto>());

        await vm.CargarAsync();

        Assert.Empty(vm.Items);
    }
}

public class ProductoFormViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ProductoFormViewModel vm, Mock<IProductoService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IProductoService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<Producto>()))
            .ReturnsAsync(1);
        svcMock
            .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Producto>());

        var navMock = new Mock<INavigationService>();
        var vm = new ProductoFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_DatosCompletos_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Codigo = "P001";
        vm.Nombre = "Producto Test";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<Producto>(p =>
            p.Codigo == "P001" && p.Nombre == "Producto Test")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Codigo = "P001";
        vm.Nombre = "Producto Test";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ProductoListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinCodigo_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();
        vm.Nombre = "Producto Test";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();
        vm.Codigo = "P001";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }
}
