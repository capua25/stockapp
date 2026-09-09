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

public class ZonaListViewModelTests
{
    private static (ZonaListViewModel vm, Mock<IZonaService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<Zona>? zonas = null)
    {
        var svcMock = new Mock<IZonaService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(zonas ?? new List<Zona>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new ZonaListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var zonas = new List<Zona>
        {
            new() { Id = 1, Nombre = "Centro" },
            new() { Id = 2, Nombre = "Norte" }
        };
        var (vm, svcMock, _, _) = Crear(zonas);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Centro", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svcMock = new Mock<IZonaService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var vm = new ZonaListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAZonaFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ZonaFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaAlFormularioEnModoEdicion()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, navMock, _) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ZonaFormViewModel>(
            It.IsAny<System.Action<ZonaFormViewModel>>()), Times.Once);
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
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaReglaDeNegocio_NoPropagaYInforma()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        var mensaje = "La zona 5 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaEntidadNoEncontrada_NoPropagaYInforma()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        var mensaje = "Zona 5 no encontrada.";
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
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, _, _) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }
}

public class ZonaFormViewModelTests
{
    private static (ZonaFormViewModel vm, Mock<IZonaService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IZonaService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<Zona>()))
            .ReturnsAsync(1);
        svcMock
            .Setup(s => s.ModificarAsync(It.IsAny<Zona>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new ZonaFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Centro";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<Zona>(z => z.Nombre == "Centro")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAlContenedorDePestanas()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Centro";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<CatalogosTareaViewModel>(), Times.Once);
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
        vm.CargarParaEditar(new Zona { Id = 3, Nombre = "Sur", Activo = true });
        vm.Nombre = "Sur (renombrada)";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<Zona>(z => z.Id == 3 && z.Nombre == "Sur (renombrada)")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_ReglaDeNegocio_MuestraMensajeSinNavegar()
    {
        var (vm, svcMock, navMock) = Crear();
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<Zona>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe una zona con el nombre 'Sur'."));
        vm.CargarParaEditar(new Zona { Id = 3, Nombre = "Sur", Activo = true });
        vm.Nombre = "Sur";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe una zona con el nombre 'Sur'.", vm.MensajeError);
        navMock.Verify(n => n.Navegar<CatalogosTareaViewModel>(), Times.Never);
    }
}
