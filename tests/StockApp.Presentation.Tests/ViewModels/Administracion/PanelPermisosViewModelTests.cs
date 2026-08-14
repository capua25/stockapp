using System.Reflection;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
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
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin1", RolUsuario.Admin, null));
        var panel = new PanelPermisosViewModel(svc.Object);
        var padre = new UsuariosAdminViewModel(svc.Object, confirm.Object, panel, session.Object);
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

    // ── Round 1 (review Task 13) — lo que la UI observa, no solo el modelo ──────────────────

    [Fact]
    public async Task CargarAsync_NotificaLasPropiedadesCompuestas_NoSoloLasBase()
    {
        // Crítico 1: CargarAsync asigna las propiedades BASE directamente (no pasa por los
        // setters de Productos/GastosYFacturas/IngresosDeCaja), así que el binding del checkbox
        // compuesto ("IsChecked={Binding Productos}", etc.) solo se re-evalúa si esas bases
        // notifican también el nombre de la compuesta vía [NotifyPropertyChangedFor]. Este test
        // se suscribe a PropertyChanged (lo que la View realmente observa), no lee las bases
        // directamente como el resto de los tests de este archivo.
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(10))
            .ReturnsAsync(new List<string>
            {
                Permisos.GestionarProductos, Permisos.RecalcularStock,
                Permisos.RegistrarGastos, Permisos.RegistrarPagos,
                Permisos.RegistrarIngresos,
            });

        var notificadas = new List<string>();
        panel.PropertyChanged += (_, e) => { if (e.PropertyName is not null) notificadas.Add(e.PropertyName); };

        padre.UsuarioSeleccionado = Dto(10, RolUsuario.Operador);
        await panel._tareaCarga;

        Assert.Contains(nameof(PanelPermisosViewModel.Productos), notificadas);
        Assert.Contains(nameof(PanelPermisosViewModel.GastosYFacturas), notificadas);
        Assert.Contains(nameof(PanelPermisosViewModel.IngresosDeCaja), notificadas);
    }

    [Fact]
    public async Task CambiarUsuarioSeleccionado_FetchDelNuevoFalla_NoQuedaConLosPermisosDelAnterior()
    {
        // Crítico 2: seleccionar al Operador A (carga OK), después al Operador B (el fetch
        // falla). Sin limpiar antes del await, el panel se queda mostrando (y podría guardar)
        // los permisos tildados de A sobre B.
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(10))
            .ReturnsAsync(new List<string> { Permisos.VerFinanzas, Permisos.GestionarProductos });
        padre.UsuarioSeleccionado = Dto(10, RolUsuario.Operador);
        await panel._tareaCarga;
        Assert.True(panel.PermisoVerFinanzas);
        Assert.True(panel.PermisoGestionarProductos);

        svc.Setup(s => s.ObtenerPermisosAsync(11))
            .ThrowsAsync(new InvalidOperationException("el servidor no respondió"));
        padre.UsuarioSeleccionado = Dto(11, RolUsuario.Operador);
        await panel._tareaCarga;

        Assert.False(panel.PermisoVerFinanzas);
        Assert.False(panel.PermisoGestionarProductos);
    }

    [Fact]
    public async Task CambiarUsuarioSeleccionado_FetchFalla_BloqueaGuardarCommand()
    {
        // Crítico 2, capa b: limpiar no alcanza — un panel destildado tampoco avisa que hubo
        // un error, y Guardar le sacaría todos los permisos al usuario si el Admin no lo nota.
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(10))
            .ThrowsAsync(new InvalidOperationException("el servidor no respondió"));

        padre.UsuarioSeleccionado = Dto(10, RolUsuario.Operador);
        await panel._tareaCarga;

        Assert.False(panel.GuardarCommand.CanExecute(null));
    }

    // ── Bug: panel desincronizado de AuthorizationService.PermisosConfigurables ─────────────
    // Diagnóstico real (2026-08-14): Permisos.cs define 17 permisos, PermisosConfigurables
    // calcula 12 asignables (17 - 5 estructurales Admin-only), pero este panel tenía 11
    // checkboxes hardcodeados a mano. Faltaba Permisos.GestionarDocumentos. Agravante: un
    // Operador NACE con documentos.gestionar (PermisosInicialesOperador lo incluye), pero
    // GuardarAsync reconstruye la lista completa desde los checkboxes visibles — así que
    // guardar CUALQUIER otro cambio se lo borraba en silencio. Evidencia real de la base:
    // usuario Prueba8 (nunca editado desde el panel) lo tiene, opverif (editado una vez) no.

    [Fact]
    public async Task GuardarAsync_TildarTodosLosCheckboxes_EnviaExactamenteLosPermisosConfigurables()
    {
        // El guardián: si mañana se agrega un permiso nuevo a Permisos.Todos como configurable
        // y se olvida el checkbox correspondiente en este ViewModel (el mismo agujero que dejó
        // afuera a GestionarDocumentos), este test tiene que reventar con un mensaje que diga
        // EXACTAMENTE cuál permiso falta — no alcanza con "algo falló". Deriva el nombre de
        // propiedad esperado ("Permiso" + nombre del campo en Permisos) por reflection, en vez
        // de mantener una lista de nombres a mano que podría desincronizarse del mismo modo.
        var (panel, padre, svc) = Crear();
        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);

        var nombrePorValorDePermiso = typeof(Permisos)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .ToDictionary(f => (string)f.GetValue(null)!, f => f.Name);

        var propiedadesEncontradas = new List<PropertyInfo>();
        var permisosSinCheckbox = new List<string>();
        foreach (var permiso in AuthorizationService.PermisosConfigurables)
        {
            var nombreEsperado = "Permiso" + nombrePorValorDePermiso[permiso];
            var prop = typeof(PanelPermisosViewModel).GetProperty(nombreEsperado, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null || prop.PropertyType != typeof(bool))
                permisosSinCheckbox.Add($"{permiso} (esperaba una propiedad bool pública '{nombreEsperado}')");
            else
                propiedadesEncontradas.Add(prop);
        }

        Assert.True(permisosSinCheckbox.Count == 0,
            "El panel no tiene checkbox para estos permisos configurables: " +
            string.Join("; ", permisosSinCheckbox));

        foreach (var prop in propiedadesEncontradas)
            prop.SetValue(panel, true);

        List<string>? enviados = null;
        svc.Setup(s => s.GuardarPermisosAsync(9, It.IsAny<IReadOnlyList<string>>()))
            .Callback<int, IReadOnlyList<string>>((_, permisos) => enviados = permisos.ToList())
            .Returns(Task.CompletedTask);

        await panel.GuardarCommand.ExecuteAsync(null);

        var faltantesEnGuardar = AuthorizationService.PermisosConfigurables.Except(enviados ?? new List<string>()).ToList();
        Assert.True(faltantesEnGuardar.Count == 0,
            "GuardarAsync no envió estos permisos configurables aunque sus checkboxes estaban tildados: " +
            string.Join(", ", faltantesEnGuardar));
        Assert.Equal(AuthorizationService.PermisosConfigurables.Count, enviados?.Count ?? -1);
    }

    [Fact]
    public async Task GuardarAsync_UsuarioYaTeniaGestionarDocumentos_NoLoBorraAlGuardarOtroCambio()
    {
        // Reproduce el agravante: cargar un usuario que YA tiene documentos.gestionar, cambiar
        // un permiso ajeno (VerReportes) y guardar. Antes del fix, GuardarAsync reconstruye la
        // lista desde los 11 checkboxes visibles y documentos.gestionar desaparece en silencio.
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(9))
            .ReturnsAsync(new List<string> { Permisos.GestionarDocumentos, Permisos.GestionarTareas });

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        await panel._tareaCarga;

        panel.PermisoVerReportes = true;

        List<string>? enviados = null;
        svc.Setup(s => s.GuardarPermisosAsync(9, It.IsAny<IReadOnlyList<string>>()))
            .Callback<int, IReadOnlyList<string>>((_, permisos) => enviados = permisos.ToList())
            .Returns(Task.CompletedTask);

        await panel.GuardarCommand.ExecuteAsync(null);

        Assert.Contains(Permisos.GestionarDocumentos, enviados ?? new List<string>());
    }

    [Fact]
    public async Task PermisoGestionarDocumentos_IdaYVuelta_CargaYGuardaCorrectamente()
    {
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(9))
            .ReturnsAsync(new List<string> { Permisos.GestionarDocumentos });

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        await panel._tareaCarga;

        Assert.True(panel.PermisoGestionarDocumentos);

        List<string>? enviados = null;
        svc.Setup(s => s.GuardarPermisosAsync(9, It.IsAny<IReadOnlyList<string>>()))
            .Callback<int, IReadOnlyList<string>>((_, permisos) => enviados = permisos.ToList())
            .Returns(Task.CompletedTask);

        await panel.GuardarCommand.ExecuteAsync(null);

        Assert.Contains(Permisos.GestionarDocumentos, enviados ?? new List<string>());
    }
}
