using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;
using ICategoriaProveedorService = StockApp.Application.Catalogo.IProveedorService;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class GastosViewModelTests
{
    private static readonly DateTime Hoy = DateTime.UtcNow;

    private static Gasto GastoDe(int id, string detalle, bool pagado = false, bool activo = true)
    {
        var gasto = new Gasto
        {
            Id = id,
            ProveedorId = 1,
            Proveedor = new Proveedor { Id = 1, Nombre = "Barraca X" },
            Detalle = detalle,
            Fecha = Hoy,
            MontoTotal = 1000m,
            FuenteFinanciamientoId = 2,
            RubroGastoId = 3,
            CondicionPago = CondicionPago.Credito,
            FechaVencimiento = Hoy.AddDays(30),
            Activo = activo,
        };
        if (pagado)
            gasto.Pagos.Add(new PagoGasto { GastoId = id, Fecha = Hoy, Monto = 1000m });
        return gasto;
    }

    private static (GastosViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<Gasto>? gastos = null)
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>()))
            .ReturnsAsync(gastos ?? new List<Gasto>());

        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Barraca X", Activo = true },
        });
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(new List<LineaPoa>());

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var vm = new GastosViewModel(
            svc.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, csv.Object, guardado.Object);
        return (vm, svc, nav, confirm);
    }

    [Fact]
    public async Task CargarAsync_PopulaFilasConEstadoCalculado()
    {
        var (vm, _, _, _) = Crear(new List<Gasto>
        {
            GastoDe(1, "Pendiente de pago"),
            GastoDe(2, "Ya pagado", pagado: true),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Filas.Count);
        Assert.Equal("Pendiente", vm.Filas[0].Estado);
        Assert.Equal("Pagada", vm.Filas[1].Estado);
        Assert.Equal("Barraca X", vm.Filas[0].ProveedorNombre);
    }

    [Fact]
    public async Task FiltroDeEstado_FiltraEnMemoria()
    {
        var (vm, _, _, _) = Crear(new List<Gasto>
        {
            GastoDe(1, "Pendiente de pago"),
            GastoDe(2, "Ya pagado", pagado: true),
        });
        await vm.CargarAsync();

        vm.EstadoSeleccionado = "Pagada";
        await vm.FiltrarCommand.ExecuteAsync(null);

        var fila = Assert.Single(vm.Filas);
        Assert.Equal("Pagada", fila.Estado);
    }

    [Fact]
    public async Task FiltrarCommand_PasaLosFiltrosAlServicio()
    {
        var (vm, svc, _, _) = Crear();
        await vm.CargarAsync();
        vm.FechaDesde = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];

        await vm.FiltrarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ListarAsync(It.Is<GastoFiltro>(f =>
            f.ProveedorId == 1 && f.FechaDesde != null)), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AnularCommand_ConConfirmacion_AnulaYRecarga()
    {
        var (vm, svc, _, _) = Crear(new List<Gasto> { GastoDe(1, "Para anular") });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        svc.Verify(s => s.AnularAsync(1), Times.Once);
        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task AnularCommand_ErrorDeRegla_SeInformaSinCrashear()
    {
        var (vm, svc, _, confirm) = Crear(new List<Gasto> { GastoDe(1, "Con pagos", pagado: true) });
        svc.Setup(s => s.AnularAsync(1))
            .ThrowsAsync(new StockApp.Domain.Exceptions.ReglaDeNegocioException("Tiene pagos activos."));
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("Tiene pagos activos."), Times.Once);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAlFormulario()
    {
        var (vm, _, nav, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<GastoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarYPagos_ConSeleccion_NaveganConElGasto()
    {
        var (vm, _, nav, _) = Crear(new List<Gasto> { GastoDe(1, "Editable") });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.EditarCommand.ExecuteAsync(null);
        await vm.PagosCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<GastoFormViewModel>(
            It.IsAny<Action<GastoFormViewModel>>()), Times.Once);
        nav.Verify(n => n.Navegar<PagosGastoViewModel>(
            It.IsAny<Action<PagosGastoViewModel>>()), Times.Once);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.PagosCommand.CanExecute(null));
        Assert.False(vm.AnularCommand.CanExecute(null));
    }
}
