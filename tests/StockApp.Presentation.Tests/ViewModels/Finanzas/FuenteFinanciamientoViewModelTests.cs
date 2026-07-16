using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FuenteFinanciamientoListViewModelTests
{
    private static (FuenteFinanciamientoListViewModel vm,
                    Mock<IFuenteFinanciamientoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<FuenteFinanciamiento>? fuentes = null)
    {
        var svcMock = new Mock<IFuenteFinanciamientoService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ReturnsAsync(fuentes ?? new List<FuenteFinanciamiento>());
        svcMock.Setup(s => s.BajaLogicaAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new FuenteFinanciamientoListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var (vm, svcMock, _, _) = Crear(new List<FuenteFinanciamiento>
        {
            new() { Id = 1, Nombre = "Literal B" },
            new() { Id = 2, Nombre = "Multas" },
        });

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Literal B", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAlFormulario()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<FuenteFinanciamientoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaAlFormularioEnModoEdicion()
    {
        var fuente = new FuenteFinanciamiento { Id = 5, Nombre = "Literal C", Activo = true };
        var (vm, _, navMock, _) = Crear(new List<FuenteFinanciamiento> { fuente });
        await vm.CargarAsync();
        vm.ItemSeleccionado = fuente;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<FuenteFinanciamientoFormViewModel>(
            It.IsAny<System.Action<FuenteFinanciamientoFormViewModel>>()), Times.Once);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ConfirmaYLlamaServicio()
    {
        var fuente = new FuenteFinanciamiento { Id = 5, Nombre = "Multas", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<FuenteFinanciamiento> { fuente });
        await vm.CargarAsync();
        vm.ItemSeleccionado = fuente;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ExcepcionDeDominio_NoPropagaYInforma()
    {
        var fuente = new FuenteFinanciamiento { Id = 5, Nombre = "Multas", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<FuenteFinanciamiento> { fuente });
        await vm.CargarAsync();
        vm.ItemSeleccionado = fuente;

        var mensaje = "La fuente de financiamiento 5 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ItemInactivo_EstaDeshabilitado()
    {
        var fuente = new FuenteFinanciamiento { Id = 5, Nombre = "Multas", Activo = false };
        var (vm, _, _, _) = Crear(new List<FuenteFinanciamiento> { fuente });
        await vm.CargarAsync();
        vm.ItemSeleccionado = fuente;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }
}

public class FuenteFinanciamientoFormViewModelTests
{
    private static (FuenteFinanciamientoFormViewModel vm,
                    Mock<IFuenteFinanciamientoService> svcMock,
                    Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IFuenteFinanciamientoService>();
        svcMock.Setup(s => s.AltaAsync(It.IsAny<FuenteFinanciamiento>())).ReturnsAsync(1);
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<FuenteFinanciamiento>())).Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new FuenteFinanciamientoFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_SinEdicion_LlamaAltaYVuelveAMaestros()
    {
        var (vm, svcMock, navMock) = Crear();
        vm.Nombre = "Literal B";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<FuenteFinanciamiento>(f => f.Nombre == "Literal B")), Times.Once);
        navMock.Verify(n => n.Navegar<MaestrosFinanzasViewModel>(), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_LlamaModificarConElId()
    {
        var (vm, svcMock, _) = Crear();
        vm.CargarParaEditar(new FuenteFinanciamiento { Id = 3, Nombre = "Literal C", Activo = true });
        vm.Nombre = "Literal C (FIGM)";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<FuenteFinanciamiento>(f => f.Id == 3 && f.Nombre == "Literal C (FIGM)")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_ReglaDeNegocio_MuestraMensajeSinNavegar()
    {
        var (vm, svcMock, navMock) = Crear();
        svcMock.Setup(s => s.AltaAsync(It.IsAny<FuenteFinanciamiento>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe una fuente de financiamiento con el nombre 'Multas'."));
        vm.Nombre = "Multas";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe una fuente de financiamiento con el nombre 'Multas'.", vm.MensajeError);
        navMock.Verify(n => n.Navegar<MaestrosFinanzasViewModel>(), Times.Never);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }
}
