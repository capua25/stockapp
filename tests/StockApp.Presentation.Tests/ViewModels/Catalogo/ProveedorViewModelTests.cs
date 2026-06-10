using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class ProveedorListViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ProveedorListViewModel vm, Mock<IProveedorService> svcMock, Mock<INavigationService> navMock)
        Crear(IReadOnlyList<Proveedor>? proveedores = null)
    {
        var svcMock = new Mock<IProveedorService>();
        svcMock
            .Setup(s => s.ListarTodosAsync())
            .ReturnsAsync(proveedores ?? new List<Proveedor>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new ProveedorListViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    // ── D5.1 tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var proveedores = new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Proveedor Uno" },
            new() { Id = 2, Nombre = "Proveedor Dos" }
        };
        var (vm, svcMock, _) = Crear(proveedores);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodosAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Proveedor Uno", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAProveedorFormViewModel()
    {
        var (vm, _, navMock) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ProveedorFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_LlamaServicio()
    {
        var prov = new Proveedor { Id = 7, Nombre = "Prov Prueba", Activo = true };
        var (vm, svcMock, _) = Crear(new List<Proveedor> { prov });
        await vm.CargarAsync();
        vm.ItemSeleccionado = prov;

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(7), Times.Once);
    }
}

public class ProveedorFormViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ProveedorFormViewModel vm, Mock<IProveedorService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IProveedorService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<Proveedor>()))
            .ReturnsAsync(1);

        var navMock = new Mock<INavigationService>();
        var vm = new ProveedorFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Distribuidor ABC";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<Proveedor>(p => p.Nombre == "Distribuidor ABC")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Distribuidor ABC";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ProveedorListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }
}
