using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class RubroGastoListViewModelTests
{
    private static (RubroGastoListViewModel vm,
                    Mock<IRubroGastoService> svcMock,
                    Mock<INavigationService> navMock)
        Crear(IReadOnlyList<RubroGasto>? rubros = null)
    {
        var svcMock = new Mock<IRubroGastoService>();
        svcMock.Setup(s => s.ListarTodosAsync()).ReturnsAsync(rubros ?? new List<RubroGasto>());

        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var vm = new RubroGastoListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var (vm, _, _) = Crear(new List<RubroGasto>
        {
            new() { Id = 1, Codigo = 1, Nombre = "Sueldos" },
            new() { Id = 2, Codigo = 3, Nombre = "Combustibles" },
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Sueldos", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAlFormulario()
    {
        var (vm, _, navMock) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<RubroGastoFormViewModel>(), Times.Once);
    }
}

public class RubroGastoFormViewModelTests
{
    private static (RubroGastoFormViewModel vm,
                    Mock<IRubroGastoService> svcMock,
                    Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IRubroGastoService>();
        svcMock.Setup(s => s.AltaAsync(It.IsAny<RubroGasto>())).ReturnsAsync(1);
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<RubroGasto>())).Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new RubroGastoFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConCodigoYNombre_LlamaAltaConElCodigoParseado()
    {
        var (vm, svcMock, navMock) = Crear();
        vm.CodigoTexto = "3";
        vm.Nombre = "Combustibles";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(
            It.Is<RubroGasto>(r => r.Codigo == 3 && r.Nombre == "Combustibles")), Times.Once);
        navMock.Verify(n => n.Navegar<MaestrosFinanzasViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_CodigoNoNumerico_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();
        vm.CodigoTexto = "abc";
        vm.Nombre = "Combustibles";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_LlamaModificar()
    {
        var (vm, svcMock, _) = Crear();
        vm.CargarParaEditar(new RubroGasto { Id = 4, Codigo = 5, Nombre = "Papelería", Activo = true });
        vm.Nombre = "Papelería y Librería";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<RubroGasto>(r => r.Id == 4 && r.Codigo == 5 && r.Nombre == "Papelería y Librería")), Times.Once);
    }
}
