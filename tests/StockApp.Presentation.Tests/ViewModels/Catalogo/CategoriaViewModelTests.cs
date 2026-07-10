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

public class CategoriaListViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (CategoriaListViewModel vm, Mock<ICategoriaService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<Categoria>? categorias = null)
    {
        var svcMock = new Mock<ICategoriaService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(categorias ?? new List<Categoria>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new CategoriaListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    // ── D4.1 tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var categorias = new List<Categoria>
        {
            new() { Id = 1, Nombre = "Electrónica" },
            new() { Id = 2, Nombre = "Ferretería" }
        };
        var (vm, svcMock, _, _) = Crear(categorias);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Electrónica", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task NuevoCommand_NavegaACategoriaFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<CategoriaFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var cat = new Categoria { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Categoria> { cat });
        await vm.CargarAsync();
        vm.ItemSeleccionado = cat;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var cat = new Categoria { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Categoria> { cat });
        await vm.CargarAsync();
        vm.ItemSeleccionado = cat;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaInvalidOperationException_NoPropagaYInforma()
    {
        var cat = new Categoria { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Categoria> { cat });
        await vm.CargarAsync();
        vm.ItemSeleccionado = cat;

        var mensaje = "La categoría 5 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        // No debe propagar la excepción (regresión del crash real).
        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaKeyNotFoundException_NoPropagaYInforma()
    {
        var cat = new Categoria { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Categoria> { cat });
        await vm.CargarAsync();
        vm.ItemSeleccionado = cat;

        var mensaje = "Categoría 5 no encontrada.";
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
        var cat = new Categoria { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<Categoria> { cat });
        await vm.CargarAsync();
        vm.ItemSeleccionado = cat;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var cat = new Categoria { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, _, _) = Crear(new List<Categoria> { cat });
        await vm.CargarAsync();
        vm.ItemSeleccionado = cat;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }
}

public class CategoriaFormViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (CategoriaFormViewModel vm, Mock<ICategoriaService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<ICategoriaService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<Categoria>()))
            .ReturnsAsync(1);

        var navMock = new Mock<INavigationService>();
        var vm = new CategoriaFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Electrónica";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<Categoria>(c => c.Nombre == "Electrónica")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Electrónica";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<CategoriaListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }
}
