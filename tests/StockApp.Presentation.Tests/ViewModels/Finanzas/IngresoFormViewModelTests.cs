using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class IngresoFormViewModelTests
{
    private static (IngresoFormViewModel vm,
                    Mock<IIngresoCajaService> svcMock,
                    Mock<INavigationService> navMock)
        Crear(RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null)
    {
        var svc = new Mock<IIngresoCajaService>();
        svc.Setup(s => s.AltaAsync(It.IsAny<IngresoCaja>())).ReturnsAsync(7);
        svc.Setup(s => s.ModificarAsync(It.IsAny<IngresoCaja>())).Returns(Task.CompletedTask);

        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 2, Nombre = "Literal B", Activo = true },
        });

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos ?? Enumerable.Empty<string>()));

        var nav = new Mock<INavigationService>();

        var vm = new IngresoFormViewModel(svc.Object, fuentes.Object, session.Object, nav.Object);
        return (vm, svc, nav);
    }

    private static async Task CompletarFormularioValidoAsync(IngresoFormViewModel vm)
    {
        await vm.InicializarAsync();
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.Concepto = "Partida mensual";
        vm.MontoTexto = "1.500,50";   // es-UY: miles con punto, decimales con coma
    }

    [Fact]
    public void FechaSeleccionada_EsDateTimeNullable_ParaBindearConCalendarDatePicker()
    {
        // Migración DatePicker (DateTimeOffset?) → CalendarDatePicker (DateTime?).
        Assert.Equal(typeof(DateTime?),
            typeof(IngresoFormViewModel).GetProperty(nameof(IngresoFormViewModel.FechaSeleccionada))!.PropertyType);
    }

    [Fact]
    public async Task InicializarAsync_CargaFuentesDisponibles()
    {
        var (vm, _, _) = Crear();

        await vm.InicializarAsync();

        var fuente = Assert.Single(vm.FuentesDisponibles);
        Assert.Equal("Literal B", fuente.Nombre);
    }

    [Fact]
    public async Task Guardar_ParseaElMontoConCulturaEsUY()
    {
        var (vm, svc, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.Is<IngresoCaja>(i =>
            i.Monto == 1500.50m && i.Concepto == "Partida mensual")), Times.Once);
    }

    [Fact]
    public async Task Guardar_MontoIlegible_MuestraErrorSinLlamarAlServicio()
    {
        var (vm, svc, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "abc";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.MensajeError);
        svc.Verify(s => s.AltaAsync(It.IsAny<IngresoCaja>()), Times.Never);
    }

    [Fact]
    public async Task Guardar_ReglaDeNegocio_MuestraMensajeError()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<IngresoCaja>()))
            .ThrowsAsync(new ReglaDeNegocioException("Fuente inválida."));
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Fuente inválida.", vm.MensajeError);
    }

    [Fact]
    public async Task Guardar_ConAltaExitosa_Navega()
    {
        var (vm, _, nav) = Crear();
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<IngresosViewModel>(), Times.Once);
    }

    [Fact]
    public async Task Guardar_FechaSeleccionada_SinCorrimientoDeDia()
    {
        // CalendarDatePicker bindea DateTime? (no DateTimeOffset?, ver migración de
        // DatePicker→CalendarDatePicker). El dominio de Finanzas no tiene componente
        // horario: la fecha elegida debe guardarse tal cual, sin conversión de huso
        // horario.
        var (vm, svc, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.FechaSeleccionada = new DateTime(2026, 6, 5);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.Is<IngresoCaja>(i =>
            i.Fecha == new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc))), Times.Once);
    }

    [Fact]
    public async Task CargarParaEditar_PrecargaCamposIncluyendoFechaSinCorrimiento()
    {
        var (vm, svc, _) = Crear();
        var ingreso = new IngresoCaja
        {
            Id = 9, Concepto = "Histórico",
            Fecha = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
            FuenteFinanciamientoId = 2, Monto = 800m,
        };
        vm.CargarParaEditar(ingreso);
        await vm.InicializarAsync();

        Assert.True(vm.EsEdicion);
        Assert.Equal("Histórico", vm.Concepto);
        Assert.Equal(new DateTime(2026, 2, 10), vm.FechaSeleccionada!.Value.Date);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ModificarAsync(It.Is<IngresoCaja>(i =>
            i.Id == 9 && i.Fecha == new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc))), Times.Once);
    }

    // ── PuedeRegistrarIngresos: gating del botón "Guardar" (bugfix 2026-08-15) ─────────────
    // El botón no tenía NINGÚN gating por permiso. Se llega desde IngresosView ("Nuevo
    // ingreso"/"Editar"), que solo exige VerFinanzas, pero IngresoCajaService.AltaAsync/
    // ModificarAsync exigen Permisos.RegistrarIngresos sin condición — un Operador con
    // VerFinanzas pero sin RegistrarIngresos llenaba el formulario y recién al guardar se
    // comía un 403.

    [Fact]
    public void Operador_ConVerFinanzasSinRegistrarIngresos_PuedeRegistrarIngresos_EsFalse()
    {
        var (vm, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarIngresos);
    }

    [Fact]
    public void Operador_ConRegistrarIngresos_PuedeRegistrarIngresos_EsTrue()
    {
        var (vm, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas, Permisos.RegistrarIngresos });

        Assert.True(vm.PuedeRegistrarIngresos);
    }

    [Fact]
    public void Admin_PuedeRegistrarIngresos_EsTrue()
    {
        var (vm, _, _) = Crear(rol: RolUsuario.Admin, permisos: Array.Empty<string>());

        Assert.True(vm.PuedeRegistrarIngresos);
    }
}
