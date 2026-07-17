using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class InicioViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (InicioViewModel vm, Mock<ICurrentSession> sessionMock, Mock<INavigationService> navMock,
                     Mock<IFinanzasVistasService> finanzasMock)
        Crear(UsuarioSesion usuario, CalendarioPagosDto? calendario = null)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.UsuarioActual).Returns(usuario);
        sessionMock.Setup(s => s.RolActual).Returns(usuario.Rol);

        var navMock = new Mock<INavigationService>();
        var finanzasMock = new Mock<IFinanzasVistasService>();
        finanzasMock.Setup(f => f.ObtenerCalendarioPagosAsync(null)).ReturnsAsync(
            calendario ?? new CalendarioPagosDto(
                new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(),
                new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        var vm = new InicioViewModel(sessionMock.Object, navMock.Object, finanzasMock.Object);
        return (vm, sessionMock, navMock, finanzasMock);
    }

    // ── Saludo ───────────────────────────────────────────────────────────────

    [Fact]
    public void Saludo_IncluyeNombreCompleto_CuandoEstaPresente()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _) = Crear(usuario);

        Assert.Contains("Juan Pérez", vm.Saludo);
    }

    [Fact]
    public void Saludo_CaeANombreUsuario_CuandoNombreCompletoEsNull()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, null);
        var (vm, _, _, _) = Crear(usuario);

        Assert.Contains("jperez", vm.Saludo);
    }

    // ── EsAdmin / RolTexto ───────────────────────────────────────────────────

    [Fact]
    public void EsAdmin_True_ConRolAdmin()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _) = Crear(usuario);

        Assert.True(vm.EsAdmin);
    }

    [Fact]
    public void EsAdmin_False_ConRolOperador()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _) = Crear(usuario);

        Assert.False(vm.EsAdmin);
    }

    [Fact]
    public void RolTexto_Administrador_ConRolAdmin()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _) = Crear(usuario);

        Assert.Equal("Administrador", vm.RolTexto);
    }

    [Fact]
    public void RolTexto_Operador_ConRolOperador()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _) = Crear(usuario);

        Assert.Equal("Operador", vm.RolTexto);
    }

    // ── Comandos de acceso rápido ────────────────────────────────────────────

    [Fact]
    public void IrAProductos_LlamaNavegar_AProductoListViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, navMock, _) = Crear(usuario);

        vm.IrAProductosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ProductoListViewModel>(), Times.Once);
    }

    [Fact]
    public void IrARegistrarEntrada_LlamaNavegar_AEntradaRegistroViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, navMock, _) = Crear(usuario);

        vm.IrARegistrarEntradaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<EntradaRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void IrARegistrarSalida_LlamaNavegar_ASalidaRegistroViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, navMock, _) = Crear(usuario);

        vm.IrARegistrarSalidaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<SalidaRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void IrAHistorialMovimientos_LlamaNavegar_AMovimientoHistorialViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, navMock, _) = Crear(usuario);

        vm.IrAHistorialMovimientosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
    }

    [Fact]
    public void IrAValorizacion_LlamaNavegar_AValorizacionViewModel()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, navMock, _) = Crear(usuario);

        vm.IrAValorizacionCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ValorizacionViewModel>(), Times.Once);
    }

    [Fact]
    public void IrAAuditoria_LlamaNavegar_AAuditoriaLogViewModel()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, navMock, _) = Crear(usuario);

        vm.IrAAuditoriaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<AuditoriaLogViewModel>(), Times.Once);
    }

    // ── Aviso de vencimientos (nuevo) ───────────────────────────────────────

    [Fact]
    public async Task CargarAsync_ConFacturasVencidas_MuestraElAviso()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _) = Crear(usuario, new CalendarioPagosDto(
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
            new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        await vm.CargarAsync();

        Assert.True(vm.MostrarAvisoVencimientos);
        Assert.Equal(1, vm.CantidadVencidas);
        Assert.Equal(0, vm.CantidadAVencer7Dias);
    }

    [Fact]
    public async Task CargarAsync_SinVencidasNiAVencer_NoMuestraElAviso()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _) = Crear(usuario);

        await vm.CargarAsync();

        Assert.False(vm.MostrarAvisoVencimientos);
    }

    [Fact]
    public async Task CargarAsync_ElServicioFalla_NoRompeYOcultaElAviso()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, finanzas) = Crear(usuario);
        finanzas.Setup(f => f.ObtenerCalendarioPagosAsync(null))
            .ThrowsAsync(new UnauthorizedAccessException());

        await vm.CargarAsync();

        Assert.False(vm.MostrarAvisoVencimientos);
    }

    [Fact]
    public async Task IrACalendarioPagos_NavegaACalendarioPagosViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, nav, _) = Crear(usuario);

        vm.IrACalendarioPagosCommand.Execute(null);

        nav.Verify(n => n.Navegar<CalendarioPagosViewModel>(), Times.Once);
    }
}
