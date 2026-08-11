using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Administracion;

public class PanelPermisosViewModelTests
{
    private static UsuarioDto Dto(int id, RolUsuario rol) => new(id, $"u{id}", null, rol, true, DateTime.UtcNow);

    private static (PanelPermisosViewModel panel, UsuariosAdminViewModel padre, Mock<IUsuarioService> svc) Crear()
    {
        var svc = new Mock<IUsuarioService>();
        var confirm = new Mock<StockApp.Presentation.Services.IConfirmacionService>();
        var panel = new PanelPermisosViewModel(svc.Object);
        var padre = new UsuariosAdminViewModel(svc.Object, confirm.Object, panel);
        return (panel, padre, svc);
    }

    [Fact]
    public async Task CambiarUsuarioSeleccionado_CargaLosPermisosDelNuevo()
    {
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(9))
            .ReturnsAsync(new List<string> { Permisos.VerFinanzas, Permisos.GestionarProductos });

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        // AlCambiarSeleccion dispara CargarAsync en fire-and-forget vía RefrescoPermisos —
        // _tareaCarga expone ese Task para esperarlo de forma determinista (pre-flight,
        // corrección A: nada de Task.Delay).
        await panel._tareaCarga;

        Assert.True(panel.PermisoVerFinanzas);
        Assert.True(panel.PermisoGestionarProductos);
        Assert.False(panel.PermisoRegistrarGastos);
    }

    [Fact]
    public async Task CambiarUsuarioSeleccionado_ObtenerPermisosAsyncFalla_NoPropagaLaExcepcion()
    {
        // Corrección B del pre-flight: sin RefrescoPermisos, esto quedaba como excepción no
        // observada. _tareaCarga nunca lanza — es el contrato de RefrescoPermisos.DispararBestEffortAsync.
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(9))
            .ThrowsAsync(new InvalidOperationException("el servidor no respondió"));

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        var ex = await Record.ExceptionAsync(() => panel._tareaCarga);

        Assert.Null(ex);
    }

    [Fact]
    public async Task CargarAsync_UsuarioAdmin_DejaTodoEnFalseSinConsultarElServicio()
    {
        var (panel, padre, svc) = Crear();
        padre.UsuarioSeleccionado = Dto(1, RolUsuario.Admin);

        await panel.CargarAsync();

        Assert.False(panel.PermisoVerFinanzas);
        svc.Verify(s => s.ObtenerPermisosAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void GastosYFacturas_AlTildar_EnciendeRegistrarGastosRegistrarPagosYVerFinanzas()
    {
        var (panel, _, _) = Crear();

        panel.GastosYFacturas = true;

        Assert.True(panel.PermisoRegistrarGastos);
        Assert.True(panel.PermisoRegistrarPagos);
        Assert.True(panel.PermisoVerFinanzas);
    }

    [Fact]
    public void GastosYFacturas_AlDestildar_ApagaGastosYPagosPeroNoVerFinanzas()
    {
        var (panel, _, _) = Crear();
        panel.PermisoVerFinanzas = true; // ej. porque Libro caja lo necesita
        panel.GastosYFacturas = true;

        panel.GastosYFacturas = false;

        Assert.False(panel.PermisoRegistrarGastos);
        Assert.False(panel.PermisoRegistrarPagos);
        Assert.True(panel.PermisoVerFinanzas);
    }

    [Fact]
    public void IngresosDeCaja_AlTildar_EnciendeRegistrarIngresosYVerFinanzas()
    {
        var (panel, _, _) = Crear();

        panel.IngresosDeCaja = true;

        Assert.True(panel.PermisoRegistrarIngresos);
        Assert.True(panel.PermisoVerFinanzas);
    }

    [Fact]
    public void Productos_AlTildar_EnciendeGestionarProductosYRecalcularStockJuntos()
    {
        var (panel, _, _) = Crear();

        panel.Productos = true;

        Assert.True(panel.PermisoGestionarProductos);
        Assert.True(panel.PermisoRecalcularStock);
    }

    [Fact]
    public async Task GuardarAsync_EnviaSoloLosPermisosTildados()
    {
        var (panel, padre, svc) = Crear();
        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        panel.PermisoVerFinanzas = true;
        panel.PermisoGestionarTareas = true;

        await panel.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.GuardarPermisosAsync(9,
            It.Is<IReadOnlyList<string>>(l =>
                l.Contains(Permisos.VerFinanzas) && l.Contains(Permisos.GestionarTareas) && l.Count == 2)),
            Times.Once);
    }

    [Fact]
    public async Task GuardarAsync_SinUsuarioSeleccionado_NoLlamaAlServicio()
    {
        var (panel, _, svc) = Crear();

        await panel.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.GuardarPermisosAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }
}
