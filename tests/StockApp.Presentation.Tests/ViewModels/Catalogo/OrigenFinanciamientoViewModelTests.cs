using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class OrigenFinanciamientoListViewModelTests
{
    private static (OrigenFinanciamientoListViewModel vm, Mock<IOrigenFinanciamientoService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<OrigenFinanciamiento>? origenes = null)
    {
        var svcMock = new Mock<IOrigenFinanciamientoService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(origenes ?? new List<OrigenFinanciamiento>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new OrigenFinanciamientoListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var origenes = new List<OrigenFinanciamiento>
        {
            new() { Id = 1, Nombre = "Presupuesto propio" },
            new() { Id = 2, Nombre = "Fondo nacional" }
        };
        var (vm, svcMock, _, _) = Crear(origenes);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Presupuesto propio", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svcMock = new Mock<IOrigenFinanciamientoService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var vm = new OrigenFinanciamientoListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAOrigenFinanciamientoFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrigenFinanciamientoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaAlFormularioEnModoEdicion()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, navMock, _) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrigenFinanciamientoFormViewModel>(
            It.IsAny<System.Action<OrigenFinanciamientoFormViewModel>>()), Times.Once);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditarCommand_ItemInactivo_EstaDeshabilitado()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaReglaDeNegocio_NoPropagaYInforma()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        var mensaje = "El origen de financiamiento 5 ya está inactivo.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaEntidadNoEncontrada_NoPropagaYInforma()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        var mensaje = "Origen de financiamiento 5 no encontrado.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new EntidadNoEncontradaException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public void BajaCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemInactivo_EstaDeshabilitado()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, _, _) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }
}

public class OrigenFinanciamientoFormViewModelTests
{
    private static (OrigenFinanciamientoFormViewModel vm, Mock<IOrigenFinanciamientoService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IOrigenFinanciamientoService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<OrigenFinanciamiento>()))
            .ReturnsAsync(1);
        svcMock
            .Setup(s => s.ModificarAsync(It.IsAny<OrigenFinanciamiento>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new OrigenFinanciamientoFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Presupuesto propio";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<OrigenFinanciamiento>(o => o.Nombre == "Presupuesto propio")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Presupuesto propio";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrigenFinanciamientoListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_LlamaModificarConElId()
    {
        var (vm, svcMock, _) = Crear();
        vm.CargarParaEditar(new OrigenFinanciamiento { Id = 3, Nombre = "Fondo nacional", Activo = true });
        vm.Nombre = "Fondo nacional (renombrado)";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<OrigenFinanciamiento>(o => o.Id == 3 && o.Nombre == "Fondo nacional (renombrado)")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_ReglaDeNegocio_MuestraMensajeSinNavegar()
    {
        var (vm, svcMock, navMock) = Crear();
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<OrigenFinanciamiento>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un origen de financiamiento con el nombre 'Fondo nacional'."));
        vm.CargarParaEditar(new OrigenFinanciamiento { Id = 3, Nombre = "Fondo nacional", Activo = true });
        vm.Nombre = "Fondo nacional";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un origen de financiamiento con el nombre 'Fondo nacional'.", vm.MensajeError);
        navMock.Verify(n => n.Navegar<OrigenFinanciamientoListViewModel>(), Times.Never);
    }
}
