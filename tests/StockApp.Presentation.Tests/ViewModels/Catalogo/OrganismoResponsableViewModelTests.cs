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

public class OrganismoResponsableListViewModelTests
{
    private static (OrganismoResponsableListViewModel vm, Mock<IOrganismoResponsableService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<OrganismoResponsable>? organismos = null)
    {
        var svcMock = new Mock<IOrganismoResponsableService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(organismos ?? new List<OrganismoResponsable>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new OrganismoResponsableListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var organismos = new List<OrganismoResponsable>
        {
            new() { Id = 1, Nombre = "Intendencia de Colonia" },
            new() { Id = 2, Nombre = "Municipio de Carmelo" }
        };
        var (vm, svcMock, _, _) = Crear(organismos);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Intendencia de Colonia", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svcMock = new Mock<IOrganismoResponsableService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var vm = new OrganismoResponsableListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAOrganismoResponsableFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrganismoResponsableFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaAlFormularioEnModoEdicion()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, navMock, _) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrganismoResponsableFormViewModel>(
            It.IsAny<System.Action<OrganismoResponsableFormViewModel>>()), Times.Once);
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
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaReglaDeNegocio_NoPropagaYInforma()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        var mensaje = "El organismo 5 ya está inactivo.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaEntidadNoEncontrada_NoPropagaYInforma()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        var mensaje = "Organismo 5 no encontrado.";
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
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, _, _) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }
}

public class OrganismoResponsableFormViewModelTests
{
    private static (OrganismoResponsableFormViewModel vm, Mock<IOrganismoResponsableService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IOrganismoResponsableService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<OrganismoResponsable>()))
            .ReturnsAsync(1);
        svcMock
            .Setup(s => s.ModificarAsync(It.IsAny<OrganismoResponsable>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new OrganismoResponsableFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Intendencia de Colonia";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<OrganismoResponsable>(o => o.Nombre == "Intendencia de Colonia")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAlContenedorDePestanas()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Intendencia de Colonia";

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
        vm.CargarParaEditar(new OrganismoResponsable { Id = 3, Nombre = "Municipio de Carmelo", Activo = true });
        vm.Nombre = "Municipio de Carmelo (renombrado)";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<OrganismoResponsable>(o => o.Id == 3 && o.Nombre == "Municipio de Carmelo (renombrado)")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_ReglaDeNegocio_MuestraMensajeSinNavegar()
    {
        var (vm, svcMock, navMock) = Crear();
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<OrganismoResponsable>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un organismo con el nombre 'Municipio de Carmelo'."));
        vm.CargarParaEditar(new OrganismoResponsable { Id = 3, Nombre = "Municipio de Carmelo", Activo = true });
        vm.Nombre = "Municipio de Carmelo";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un organismo con el nombre 'Municipio de Carmelo'.", vm.MensajeError);
        navMock.Verify(n => n.Navegar<CatalogosTareaViewModel>(), Times.Never);
    }
}
