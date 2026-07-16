using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class LineaPoaListViewModelTests
{
    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var svcMock = new Mock<ILineaPoaService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa>
        {
            new() { Id = 1, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 },
        });
        var vm = new LineaPoaListViewModel(
            svcMock.Object, new Mock<INavigationService>().Object, new Mock<IConfirmacionService>().Object);

        await vm.CargarAsync();

        Assert.Single(vm.Items);
        Assert.Equal("Rambla", vm.Items[0].Nombre);
    }
}

public class LineaPoaFormViewModelTests
{
    private static (LineaPoaFormViewModel vm,
                    Mock<ILineaPoaService> svcMock,
                    Mock<IFuenteFinanciamientoService> fuentesMock,
                    Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<ILineaPoaService>();
        svcMock.Setup(s => s.AltaAsync(It.IsAny<LineaPoa>())).ReturnsAsync(1);
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<LineaPoa>())).Returns(Task.CompletedTask);

        var fuentesMock = new Mock<IFuenteFinanciamientoService>();
        fuentesMock.Setup(s => s.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 1, Nombre = "Literal B", Activo = true },
            new() { Id = 2, Nombre = "Literal C", Activo = true },
        });

        var navMock = new Mock<INavigationService>();
        var vm = new LineaPoaFormViewModel(svcMock.Object, fuentesMock.Object, navMock.Object);
        return (vm, svcMock, fuentesMock, navMock);
    }

    [Fact]
    public async Task InicializarAsync_CargaFuentesDisponiblesYUnaFilaVacia()
    {
        var (vm, _, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.Equal(2, vm.FuentesDisponibles.Count);
        Assert.Single(vm.Asignaciones);  // arranca con una fila lista para completar
    }

    [Fact]
    public async Task AgregarYQuitarAsignacion_ModificanLaColeccion()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();

        vm.AgregarAsignacionCommand.Execute(null);
        Assert.Equal(2, vm.Asignaciones.Count);

        vm.QuitarAsignacionCommand.Execute(vm.Asignaciones[1]);
        Assert.Single(vm.Asignaciones);
    }

    [Fact]
    public async Task GuardarCommand_ConDatosValidos_LlamaAltaConLasAsignaciones()
    {
        var (vm, svcMock, _, navMock) = Crear();
        await vm.InicializarAsync();
        vm.Nombre = "COMPOSTERAS";
        vm.Programa = "Ambiente";
        vm.EjercicioTexto = "2026";
        vm.Asignaciones[0].FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.Asignaciones[0].MontoTexto = "100000";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<LineaPoa>(l =>
            l.Nombre == "COMPOSTERAS"
            && l.Ejercicio == 2026
            && l.Asignaciones.Count == 1
            && l.Asignaciones[0].FuenteFinanciamientoId == 1
            && l.Asignaciones[0].Monto == 100000m)), Times.Once);
        navMock.Verify(n => n.Navegar<MaestrosFinanzasViewModel>(), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_AsignacionSinFuente_MuestraErrorSinLlamarServicio()
    {
        var (vm, svcMock, _, _) = Crear();
        await vm.InicializarAsync();
        vm.Nombre = "PRENSA";
        vm.Programa = "Comunicación";
        vm.EjercicioTexto = "2026";
        vm.Asignaciones[0].MontoTexto = "100";  // sin FuenteSeleccionada

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.MensajeError);
        svcMock.Verify(s => s.AltaAsync(It.IsAny<LineaPoa>()), Times.Never);
    }

    [Fact]
    public async Task CargarParaEditar_PrecargaCamposYAsignaciones()
    {
        var (vm, svcMock, _, _) = Crear();
        vm.CargarParaEditar(new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026, Activo = true,
            Asignaciones =
            {
                new AsignacionPresupuestal
                {
                    Id = 10, LineaPoaId = 4, FuenteFinanciamientoId = 2, Monto = 50000m,
                    FuenteFinanciamiento = new FuenteFinanciamiento { Id = 2, Nombre = "Literal C" },
                },
            },
        });
        await vm.InicializarAsync();

        Assert.True(vm.EsEdicion);
        Assert.Equal("COMPOSTERAS", vm.Nombre);
        Assert.Equal("2026", vm.EjercicioTexto);
        var fila = Assert.Single(vm.Asignaciones);
        Assert.Equal(2, fila.FuenteSeleccionada!.Id);
        Assert.Equal("50000", fila.MontoTexto);

        vm.Nombre = "COMPOSTERAS II";
        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.ModificarAsync(It.Is<LineaPoa>(l =>
            l.Id == 4 && l.Nombre == "COMPOSTERAS II" && l.Asignaciones.Count == 1)), Times.Once);
    }

    [Fact]
    public void GuardarCommand_EjercicioNoNumerico_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();
        vm.Nombre = "Rambla";
        vm.Programa = "Obras";
        vm.EjercicioTexto = "no-es-un-año";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarCommand_MontoConSeparadorIncorrectoParaLaCultura_MuestraErrorSinGuardar()
    {
        // "1500.50" con cultura es-UY (coma decimal) NO debe parsear como 150050: el "."
        // no es un separador reconocido bajo AllowDecimalPoint con esta cultura, así que
        // debe fallar el TryParse y mostrar el MensajeError existente, no corromper el monto.
        var (vm, svcMock, _, _) = Crear();
        await vm.InicializarAsync();
        vm.Nombre = "COMPOSTERAS";
        vm.Programa = "Ambiente";
        vm.EjercicioTexto = "2026";
        vm.Asignaciones[0].FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.Asignaciones[0].MontoTexto = "1500.50";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.MensajeError);
        svcMock.Verify(s => s.AltaAsync(It.IsAny<LineaPoa>()), Times.Never);
    }

    [Fact]
    public async Task GuardarCommand_MontoConComaDecimal_GuardaElValorCorrecto()
    {
        var (vm, svcMock, _, _) = Crear();
        await vm.InicializarAsync();
        vm.Nombre = "COMPOSTERAS";
        vm.Programa = "Ambiente";
        vm.EjercicioTexto = "2026";
        vm.Asignaciones[0].FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.Asignaciones[0].MontoTexto = "1500,50";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<LineaPoa>(l =>
            l.Asignaciones.Count == 1 && l.Asignaciones[0].Monto == 1500.50m)), Times.Once);
    }

    [Fact]
    public async Task CargarParaEditar_RoundTripDeEdicion_ConservaElMonto()
    {
        // El monto formateado por InicializarAsync (con la misma cultura fija) debe poder
        // volver a parsearse tal cual al guardar, sin perder precisión ni corromperse.
        var (vm, svcMock, _, _) = Crear();
        vm.CargarParaEditar(new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026, Activo = true,
            Asignaciones =
            {
                new AsignacionPresupuestal
                {
                    Id = 10, LineaPoaId = 4, FuenteFinanciamientoId = 2, Monto = 1500.50m,
                    FuenteFinanciamiento = new FuenteFinanciamiento { Id = 2, Nombre = "Literal C" },
                },
            },
        });
        await vm.InicializarAsync();

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.ModificarAsync(It.Is<LineaPoa>(l =>
            l.Asignaciones.Count == 1 && l.Asignaciones[0].Monto == 1500.50m)), Times.Once);
    }
}
