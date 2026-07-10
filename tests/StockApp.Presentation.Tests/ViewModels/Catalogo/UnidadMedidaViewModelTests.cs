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

public class UnidadMedidaListViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (UnidadMedidaListViewModel vm, Mock<IUnidadMedidaService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<UnidadMedida>? unidades = null)
    {
        var svcMock = new Mock<IUnidadMedidaService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(unidades ?? new List<UnidadMedida>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new UnidadMedidaListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    // ── D6.1 tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var unidades = new List<UnidadMedida>
        {
            new() { Id = 1, Nombre = "Unidad", Abreviatura = "u" },
            new() { Id = 2, Nombre = "Kilogramo", Abreviatura = "kg" }
        };
        var (vm, svcMock, _, _) = Crear(unidades);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Unidad", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAUnidadMedidaFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<UnidadMedidaFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var um = new UnidadMedida { Id = 3, Nombre = "Metro", Abreviatura = "m", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<UnidadMedida> { um });
        await vm.CargarAsync();
        vm.ItemSeleccionado = um;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(3), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var um = new UnidadMedida { Id = 3, Nombre = "Metro", Abreviatura = "m", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<UnidadMedida> { um });
        await vm.CargarAsync();
        vm.ItemSeleccionado = um;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaInvalidOperationException_NoPropagaYInforma()
    {
        var um = new UnidadMedida { Id = 3, Nombre = "Metro", Abreviatura = "m", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<UnidadMedida> { um });
        await vm.CargarAsync();
        vm.ItemSeleccionado = um;

        var mensaje = "La unidad de medida 3 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(3)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        // No debe propagar la excepción (regresión del crash real).
        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaKeyNotFoundException_NoPropagaYInforma()
    {
        var um = new UnidadMedida { Id = 3, Nombre = "Metro", Abreviatura = "m", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<UnidadMedida> { um });
        await vm.CargarAsync();
        vm.ItemSeleccionado = um;

        var mensaje = "UnidadMedida 3 no encontrada.";
        svcMock.Setup(s => s.BajaLogicaAsync(3)).ThrowsAsync(new EntidadNoEncontradaException(mensaje));

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
        var um = new UnidadMedida { Id = 3, Nombre = "Metro", Abreviatura = "m", Activo = false };
        var (vm, _, _, _) = Crear(new List<UnidadMedida> { um });
        await vm.CargarAsync();
        vm.ItemSeleccionado = um;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var um = new UnidadMedida { Id = 3, Nombre = "Metro", Abreviatura = "m", Activo = true };
        var (vm, _, _, _) = Crear(new List<UnidadMedida> { um });
        await vm.CargarAsync();
        vm.ItemSeleccionado = um;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }
}

public class UnidadMedidaFormViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (UnidadMedidaFormViewModel vm, Mock<IUnidadMedidaService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IUnidadMedidaService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<UnidadMedida>()))
            .ReturnsAsync(1);

        var navMock = new Mock<INavigationService>();
        var vm = new UnidadMedidaFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConDatosCompletos_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Litro";
        vm.Abreviatura = "l";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<UnidadMedida>(u =>
            u.Nombre == "Litro" && u.Abreviatura == "l")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Litro";
        vm.Abreviatura = "l";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<UnidadMedidaListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();
        vm.Abreviatura = "l";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public void GuardarCommand_SinAbreviatura_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();
        vm.Nombre = "Litro";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }
}
