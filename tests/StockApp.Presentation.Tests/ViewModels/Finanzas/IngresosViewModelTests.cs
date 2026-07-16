using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class IngresosViewModelTests
{
    private static IngresoCaja Ingreso(int id, string concepto, bool activo = true) => new()
    {
        Id = id, Fecha = DateTime.UtcNow, Concepto = concepto,
        FuenteFinanciamientoId = 2,
        FuenteFinanciamiento = new FuenteFinanciamiento { Id = 2, Nombre = "Literal B" },
        Monto = 1000m, Activo = activo,
    };

    private static (IngresosViewModel vm,
                    Mock<IIngresoCajaService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<IngresoCaja>? ingresos = null)
    {
        var svc = new Mock<IIngresoCajaService>();
        svc.Setup(s => s.ListarTodosAsync()).ReturnsAsync(ingresos ?? new List<IngresoCaja>());
        svc.Setup(s => s.BajaLogicaAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new IngresosViewModel(svc.Object, nav.Object, confirm.Object);
        return (vm, svc, nav, confirm);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var (vm, _, _, _) = Crear(new List<IngresoCaja>
        {
            Ingreso(1, "Partida FIGM"), Ingreso(2, "Multas"),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Partida FIGM", vm.Items[0].Concepto);
    }

    // ── ItemsView: fix de ordenamiento por click en encabezados (Avalonia 12, regresión #21129) ──

    [Fact]
    public async Task ItemsView_EsOrdenable()
    {
        var (vm, _, _, _) = Crear(new List<IngresoCaja>
        {
            Ingreso(1, "Partida FIGM"), Ingreso(2, "Multas"),
        });

        await vm.CargarAsync();

        Assert.NotNull(vm.ItemsView);
        Assert.IsType<DataGridCollectionView>(vm.ItemsView);
        Assert.True(vm.ItemsView.CanSort);
    }

    [Fact]
    public async Task ItemsView_TrasCargarAsync_ReflejaLosItems()
    {
        var (vm, _, _, _) = Crear(new List<IngresoCaja>
        {
            Ingreso(1, "Partida FIGM"), Ingreso(2, "Multas"),
        });

        await vm.CargarAsync();

        Assert.Equal(vm.Items.Count, vm.ItemsView.Cast<IngresoCaja>().Count());
    }

    [Fact]
    public async Task NuevoCommand_NavegaAlFormulario()
    {
        var (vm, _, nav, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<IngresoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaEnModoEdicion()
    {
        var ingreso = Ingreso(5, "Editable");
        var (vm, _, nav, _) = Crear(new List<IngresoCaja> { ingreso });
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];

        await vm.EditarCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<IngresoFormViewModel>(
            It.IsAny<Action<IngresoFormViewModel>>()), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_PideConfirmacionConMontoFormateado()
    {
        // Bug real (verificación orgánica): el mensaje mostraba el decimal crudo
        // ("2000.7500") en vez del formato moneda es-UY que usan las grillas ("$ 2.000,75").
        var ingreso = Ingreso(1, "Para baja");
        ingreso.Monto = 2000.7500m;
        var (vm, _, _, confirm) = Crear(new List<IngresoCaja> { ingreso });
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];

        await vm.BajaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(
            s => s.Contains("$ 2.000,75") && !s.Contains("2000.7500"))), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ConConfirmacion_DaDeBajaYRecarga()
    {
        var (vm, svc, _, _) = Crear(new List<IngresoCaja> { Ingreso(1, "Para baja") });
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];

        await vm.BajaCommand.ExecuteAsync(null);

        svc.Verify(s => s.BajaLogicaAsync(1), Times.Once);
        svc.Verify(s => s.ListarTodosAsync(), Times.AtLeast(2));
    }

    [Fact]
    public async Task BajaCommand_ErrorDeRegla_SeInforma()
    {
        var (vm, svc, _, confirm) = Crear(new List<IngresoCaja> { Ingreso(1, "Ya inactivo", activo: false) });
        svc.Setup(s => s.BajaLogicaAsync(1))
            .ThrowsAsync(new ReglaDeNegocioException("Ya está dado de baja."));
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];
        // El CanExecute exige Activo: se fuerza el caso llamando con item inactivo re-seleccionado
        vm.Items[0].Activo = true;

        await vm.BajaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("Ya está dado de baja."), Times.Once);
    }

    [Fact]
    public void EditarYBaja_SinSeleccion_Deshabilitados()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.BajaCommand.CanExecute(null));
    }
}
