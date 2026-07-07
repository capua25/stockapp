using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class ProveedorListViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ProveedorListViewModel vm, Mock<IProveedorService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
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

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new ProveedorListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
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
        var (vm, svcMock, _, _) = Crear(proveedores);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodosAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Proveedor Uno", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAProveedorFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ProveedorFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var prov = new Proveedor { Id = 7, Nombre = "Prov Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Proveedor> { prov });
        await vm.CargarAsync();
        vm.ItemSeleccionado = prov;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(7), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var prov = new Proveedor { Id = 7, Nombre = "Prov Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Proveedor> { prov });
        await vm.CargarAsync();
        vm.ItemSeleccionado = prov;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaInvalidOperationException_NoPropagaYInforma()
    {
        var prov = new Proveedor { Id = 7, Nombre = "Prov Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Proveedor> { prov });
        await vm.CargarAsync();
        vm.ItemSeleccionado = prov;

        var mensaje = "El proveedor 7 ya está inactivo.";
        svcMock.Setup(s => s.BajaLogicaAsync(7)).ThrowsAsync(new InvalidOperationException(mensaje));

        // No debe propagar la excepción (regresión del crash real).
        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaKeyNotFoundException_NoPropagaYInforma()
    {
        var prov = new Proveedor { Id = 7, Nombre = "Prov Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Proveedor> { prov });
        await vm.CargarAsync();
        vm.ItemSeleccionado = prov;

        var mensaje = "Proveedor 7 no encontrado.";
        svcMock.Setup(s => s.BajaLogicaAsync(7)).ThrowsAsync(new KeyNotFoundException(mensaje));

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
        var prov = new Proveedor { Id = 7, Nombre = "Prov Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<Proveedor> { prov });
        await vm.CargarAsync();
        vm.ItemSeleccionado = prov;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var prov = new Proveedor { Id = 7, Nombre = "Prov Prueba", Activo = true };
        var (vm, _, _, _) = Crear(new List<Proveedor> { prov });
        await vm.CargarAsync();
        vm.ItemSeleccionado = prov;

        Assert.True(vm.BajaCommand.CanExecute(null));
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
