using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Administracion;

public class UsuariosAdminViewModelTests
{
    /// <summary>Id del Admin "logueado" en todos los tests que usan Crear() (Round 2, review
    /// Task 12) — Dto(1, ...) representa "el usuario de la sesión actual" en los tests de
    /// auto-cambio de contraseña; cualquier otro id (ej. 2) representa "otro usuario".</summary>
    private static Mock<ICurrentSession> CrearSesionAdmin(int id = 1)
    {
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(id, $"usuario{id}", RolUsuario.Admin, null));
        return session;
    }

    private static (UsuariosAdminViewModel vm, Mock<IUsuarioService> svc, Mock<IConfirmacionService> confirm) Crear()
    {
        var svc = new Mock<IUsuarioService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        var session = CrearSesionAdmin();
        var vm = new UsuariosAdminViewModel(svc.Object, confirm.Object, new PanelPermisosViewModel(svc.Object, confirm.Object), session.Object);
        return (vm, svc, confirm);
    }

    private static UsuarioDto Dto(int id, RolUsuario rol, bool activo = true) =>
        new(id, $"usuario{id}", null, rol, activo, DateTime.UtcNow);

    [Fact]
    public async Task CargarAsync_PueblaItemsDesdeElServicio()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto> { Dto(1, RolUsuario.Admin), Dto(2, RolUsuario.Operador) });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Items.Count);
    }

    // ── bugfix "pantalla muda ante un 403": CargarAsync no debe escalar un 403/401, y debe dejar
    // un estado bindeable para que la vista muestre EstadoVacio (ver ViewModelBase.
    // EjecutarCargaProtegidaAsync). ──
    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaYDejaSinPermiso()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.ListarAsync()).ThrowsAsync(new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeSinPermiso));
    }

    [Fact]
    public void UsuarioSeleccionado_Admin_EsAdminSeleccionadoEsTrue()
    {
        var (vm, _, _) = Crear();

        vm.UsuarioSeleccionado = Dto(1, RolUsuario.Admin);

        Assert.True(vm.EsAdminSeleccionado);
    }

    [Fact]
    public void UsuarioSeleccionado_Operador_EsAdminSeleccionadoEsFalse()
    {
        var (vm, _, _) = Crear();

        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        Assert.False(vm.EsAdminSeleccionado);
    }

    [Fact]
    public async Task AltaAsync_LlamaAlServicioConLosCamposCargados_YRecarga()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaUsuarioAsync("nuevo", "Nombre Completo", "pwd12345", RolUsuario.Operador))
            .ReturnsAsync(9);
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.NuevoNombreUsuario = "nuevo";
        vm.NuevoNombreCompleto = "Nombre Completo";
        vm.NuevaContrasenaPlan = "pwd12345";
        vm.NuevoRol = RolUsuario.Operador;

        await vm.AltaCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaUsuarioAsync("nuevo", "Nombre Completo", "pwd12345", RolUsuario.Operador), Times.Once);
        svc.Verify(s => s.ListarAsync(), Times.Once);
        Assert.Equal(string.Empty, vm.NuevoNombreUsuario);
    }

    [Fact]
    public async Task AltaAsync_NombreDuplicado_MuestraMensajeErrorSinRecargar()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaUsuarioAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<RolUsuario>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un usuario con ese nombre."));
        vm.NuevoNombreUsuario = "repetido";
        vm.NuevaContrasenaPlan = "pwd12345";

        await vm.AltaCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un usuario con ese nombre.", vm.MensajeError);
        svc.Verify(s => s.ListarAsync(), Times.Never);
    }

    [Fact]
    public async Task BajaAsync_ConConfirmacion_LlamaAlServicioYRecarga()
    {
        var (vm, svc, confirm) = Crear();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.BajaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svc.Verify(s => s.BajaLogicaAsync(2), Times.Once);
    }

    [Fact]
    public async Task BajaAsync_SinSeleccion_NoLlamaAlServicio()
    {
        var (vm, svc, _) = Crear();

        await vm.BajaCommand.ExecuteAsync(null);

        svc.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CambiarRolAsync_LlamaAlServicioConElUsuarioSeleccionadoYRecarga()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.CambiarRolCommand.ExecuteAsync(RolUsuario.Admin);

        svc.Verify(s => s.CambiarRolAsync(2, RolUsuario.Admin), Times.Once);
    }

    [Fact]
    public async Task CambiarContrasenaAsync_LlamaAlServicioConLaContrasenaCargadaYLimpiaElCampo()
    {
        var (vm, svc, _) = Crear();
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);
        vm.NuevaContrasenaParaSeleccionado = "otraClave123";

        await vm.CambiarContrasenaCommand.ExecuteAsync(null);

        svc.Verify(s => s.CambiarContrasenaAsync(2, "otraClave123", null), Times.Once);
        Assert.Equal(string.Empty, vm.NuevaContrasenaParaSeleccionado);
    }

    // ── Round 1 (review Task 12) ──────────────────────────────────────────────

    [Fact]
    public async Task CambiarRolAsync_UsuarioYaNoExiste_InformaAlUsuarioSinExplotar()
    {
        var (vm, svc, confirm) = Crear();
        var mensaje = "El usuario ya no existe.";
        svc.Setup(s => s.CambiarRolAsync(2, RolUsuario.Admin))
            .ThrowsAsync(new EntidadNoEncontradaException(mensaje));
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.CambiarRolCommand.ExecuteAsync(RolUsuario.Admin);

        confirm.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task CambiarContrasenaAsync_UsuarioYaNoExiste_InformaAlUsuarioSinExplotar()
    {
        var (vm, svc, confirm) = Crear();
        var mensaje = "El usuario ya no existe.";
        svc.Setup(s => s.CambiarContrasenaAsync(2, "otraClave123", null))
            .ThrowsAsync(new EntidadNoEncontradaException(mensaje));
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);
        vm.NuevaContrasenaParaSeleccionado = "otraClave123";

        await vm.CambiarContrasenaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task CambiarContrasenaAsync_PideConfirmacionAntesDeLlamarAlServicio()
    {
        var (vm, svc, confirm) = Crear();
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);
        vm.NuevaContrasenaParaSeleccionado = "otraClave123";

        await vm.CambiarContrasenaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CambiarContrasenaAsync_SinConfirmacion_NoLlamaAlServicioNiLimpiaElCampo()
    {
        var svc = new Mock<IUsuarioService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        var vm = new UsuariosAdminViewModel(
            svc.Object, confirm.Object, new PanelPermisosViewModel(svc.Object, confirm.Object), CrearSesionAdmin().Object);
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);
        vm.NuevaContrasenaParaSeleccionado = "otraClave123";

        await vm.CambiarContrasenaCommand.ExecuteAsync(null);

        svc.Verify(s => s.CambiarContrasenaAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        Assert.Equal("otraClave123", vm.NuevaContrasenaParaSeleccionado);
    }

    [Fact]
    public async Task MensajeError_SeLimpiaAlIniciarOtraOperacion()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaUsuarioAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<RolUsuario>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un usuario con ese nombre."));
        vm.NuevoNombreUsuario = "repetido";
        vm.NuevaContrasenaPlan = "pwd12345";
        await vm.AltaCommand.ExecuteAsync(null);
        Assert.NotNull(vm.MensajeError);

        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.BajaCommand.ExecuteAsync(null);

        Assert.Null(vm.MensajeError);
    }

    // ── Round 2 (review Task 12) ────────────────────────────────────────────────

    [Fact]
    public async Task CambiarContrasenaAsync_SeleccionaSuPropioUsuario_NoLlamaAlServicioYExplicaPorQue()
    {
        var (vm, svc, confirm) = Crear();
        // Dto(1, ...) coincide con el Id que CrearSesionAdmin() le da al usuario de la sesión.
        vm.UsuarioSeleccionado = Dto(1, RolUsuario.Admin);
        vm.NuevaContrasenaParaSeleccionado = "otraClave123";

        await vm.CambiarContrasenaCommand.ExecuteAsync(null);

        svc.Verify(s => s.CambiarContrasenaAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        confirm.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
        confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CambiarContrasenaAsync_ErrorDeAutorizacion_NoExplotaYNoMuestraUnSegundoAviso()
    {
        // Fix (Task 15, Round 1): el 403 ya dispara el aviso central (App.axaml.cs) — el
        // catch local NO debe mostrar un segundo InformarAsync con el ex.Message crudo del
        // servidor. Solo tiene que evitar que la excepción escape del comando.
        var (vm, svc, confirm) = Crear();
        var mensaje = "Prohibido.";
        svc.Setup(s => s.CambiarContrasenaAsync(2, "otraClave123", null))
            .ThrowsAsync(new UnauthorizedAccessException(mensaje));
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);
        vm.NuevaContrasenaParaSeleccionado = "otraClave123";

        await vm.CambiarContrasenaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    // ── Task 15: 403 en pleno vuelo (Admin te revoca el permiso a mitad de sesión) ──────────

    [Fact]
    public async Task AltaAsync_ErrorDeAutorizacion_MuestraMensajeErrorSinExplotar()
    {
        var (vm, svc, _) = Crear();
        var mensaje = "Prohibido.";
        svc.Setup(s => s.AltaUsuarioAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<RolUsuario>()))
            .ThrowsAsync(new UnauthorizedAccessException(mensaje));
        vm.NuevoNombreUsuario = "nuevo";
        vm.NuevaContrasenaPlan = "pwd12345";

        await vm.AltaCommand.ExecuteAsync(null);

        Assert.Equal(mensaje, vm.MensajeError);
    }

    [Fact]
    public async Task BajaAsync_ErrorDeAutorizacion_NoExplotaYNoMuestraUnSegundoAviso()
    {
        // Fix (Task 15, Round 1): mismo motivo que CambiarContrasenaAsync — el aviso central
        // del 403 ya avisó, el catch local no debe duplicarlo.
        var (vm, svc, confirm) = Crear();
        var mensaje = "Prohibido.";
        svc.Setup(s => s.BajaLogicaAsync(2)).ThrowsAsync(new UnauthorizedAccessException(mensaje));
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.BajaCommand.ExecuteAsync(null);

        // PreguntarAsync (la confirmación "¿Confirma dar de baja...?") sí se llama antes del
        // catch — el Never de acá apunta puntualmente al InformarAsync posterior al error.
        confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CambiarRolAsync_ErrorDeAutorizacion_NoExplotaYNoMuestraUnSegundoAviso()
    {
        // Fix (Task 15, Round 1): mismo motivo que CambiarContrasenaAsync.
        var (vm, svc, confirm) = Crear();
        var mensaje = "Prohibido.";
        svc.Setup(s => s.CambiarRolAsync(2, RolUsuario.Admin))
            .ThrowsAsync(new UnauthorizedAccessException(mensaje));
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.CambiarRolCommand.ExecuteAsync(RolUsuario.Admin);

        confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MensajeError_SeLimpiaAlIniciarCambiarRol()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaUsuarioAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<RolUsuario>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un usuario con ese nombre."));
        vm.NuevoNombreUsuario = "repetido";
        vm.NuevaContrasenaPlan = "pwd12345";
        await vm.AltaCommand.ExecuteAsync(null);
        Assert.NotNull(vm.MensajeError);

        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.CambiarRolCommand.ExecuteAsync(RolUsuario.Admin);

        Assert.Null(vm.MensajeError);
    }

    [Fact]
    public async Task MensajeError_SeLimpiaAlIniciarCambiarContrasena()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaUsuarioAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<RolUsuario>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un usuario con ese nombre."));
        vm.NuevoNombreUsuario = "repetido";
        vm.NuevaContrasenaPlan = "pwd12345";
        await vm.AltaCommand.ExecuteAsync(null);
        Assert.NotNull(vm.MensajeError);

        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);
        vm.NuevaContrasenaParaSeleccionado = "otraClave123";

        await vm.CambiarContrasenaCommand.ExecuteAsync(null);

        Assert.Null(vm.MensajeError);
    }

    // ── Bug reportado por uso real (2026-08-16): además del panel de permisos, los otros
    // botones de acción ("Dar de baja", "Hacer Admin"/"Hacer Operador", "Cambiar contraseña")
    // seguían habilitados para un usuario ya dado de baja. Sin un método de "reactivar" en
    // IUsuarioService, no hay ningún camino de vuelta -- así que las tres acciones son
    // igual de nonsense sobre un usuario inactivo: "Dar de baja" es redundante (ya está de
    // baja), y cambiar su rol o su contraseña no tiene efecto porque no puede volver a entrar.
    // Mismo criterio que ya usa el repo en Categoría/Proveedor/Producto (PuedeEditar =>
    // PuedeDarBaja: "solo se edita un ítem seleccionado y activo").

    [Fact]
    public void UsuarioSeleccionado_Inactivo_ComandosDeAccion_QuedanDeshabilitados()
    {
        var (vm, _, _) = Crear();

        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador, activo: false);

        Assert.False(vm.BajaCommand.CanExecute(null));
        Assert.False(vm.CambiarRolCommand.CanExecute(RolUsuario.Admin));
        Assert.False(vm.CambiarContrasenaCommand.CanExecute(null));
    }

    [Fact]
    public void UsuarioSeleccionado_Activo_ComandosDeAccion_QuedanHabilitados()
    {
        var (vm, _, _) = Crear();

        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador, activo: true);

        Assert.True(vm.BajaCommand.CanExecute(null));
        Assert.True(vm.CambiarRolCommand.CanExecute(RolUsuario.Admin));
        Assert.True(vm.CambiarContrasenaCommand.CanExecute(null));
    }

    // ── El caso del refresco: dar de baja al usuario seleccionado desde esta misma pantalla
    // no actualizaba UsuarioSeleccionado -- AlCambiarSeleccion en PanelPermisosViewModel solo
    // reacciona al cambio de REFERENCIA de UsuarioSeleccionado, no al cambio de su propiedad
    // Activo, así que el panel (y ahora también estos tres comandos) quedaban leyendo el
    // estado viejo (Activo=true) hasta que el Admin volvía a tocar la lista a mano.

    [Fact]
    public async Task BajaAsync_ConConfirmacion_ActualizaUsuarioSeleccionadoReflejandoLaBaja()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.ListarAsync())
            .ReturnsAsync(new List<UsuarioDto> { Dto(2, RolUsuario.Operador, activo: false) });
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador, activo: true);

        await vm.BajaCommand.ExecuteAsync(null);

        Assert.NotNull(vm.UsuarioSeleccionado);
        Assert.False(vm.UsuarioSeleccionado!.Activo);
    }
}
