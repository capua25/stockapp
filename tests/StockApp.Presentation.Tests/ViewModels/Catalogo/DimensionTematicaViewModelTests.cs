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

public class DimensionTematicaListViewModelTests
{
    private static (DimensionTematicaListViewModel vm, Mock<IDimensionTematicaService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<DimensionTematica>? dimensiones = null)
    {
        var svcMock = new Mock<IDimensionTematicaService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(dimensiones ?? new List<DimensionTematica>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new DimensionTematicaListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var dimensiones = new List<DimensionTematica>
        {
            new() { Id = 1, Nombre = "Infraestructura" },
            new() { Id = 2, Nombre = "Tránsito" }
        };
        var (vm, svcMock, _, _) = Crear(dimensiones);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Infraestructura", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svcMock = new Mock<IDimensionTematicaService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var vm = new DimensionTematicaListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task NuevoCommand_NavegaADimensionTematicaFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<DimensionTematicaFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaAlFormularioEnModoEdicion()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, navMock, _) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<DimensionTematicaFormViewModel>(
            It.IsAny<System.Action<DimensionTematicaFormViewModel>>()), Times.Once);
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
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaReglaDeNegocio_NoPropagaYInforma()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        var mensaje = "La dimensión 5 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaEntidadNoEncontrada_NoPropagaYInforma()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        var mensaje = "Dimensión 5 no encontrada.";
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
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, _, _) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }
}

public class DimensionTematicaFormViewModelTests
{
    private static (DimensionTematicaFormViewModel vm, Mock<IDimensionTematicaService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IDimensionTematicaService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<DimensionTematica>()))
            .ReturnsAsync(1);
        svcMock
            .Setup(s => s.ModificarAsync(It.IsAny<DimensionTematica>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new DimensionTematicaFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Infraestructura";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<DimensionTematica>(d => d.Nombre == "Infraestructura")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Infraestructura";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<DimensionTematicaListViewModel>(), Times.Once);
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
        vm.CargarParaEditar(new DimensionTematica { Id = 3, Nombre = "Tránsito", Activo = true });
        vm.Nombre = "Tránsito (renombrada)";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<DimensionTematica>(d => d.Id == 3 && d.Nombre == "Tránsito (renombrada)")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_ReglaDeNegocio_MuestraMensajeSinNavegar()
    {
        var (vm, svcMock, navMock) = Crear();
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<DimensionTematica>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe una dimensión con el nombre 'Tránsito'."));
        vm.CargarParaEditar(new DimensionTematica { Id = 3, Nombre = "Tránsito", Activo = true });
        vm.Nombre = "Tránsito";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe una dimensión con el nombre 'Tránsito'.", vm.MensajeError);
        navMock.Verify(n => n.Navegar<DimensionTematicaListViewModel>(), Times.Never);
    }
}
