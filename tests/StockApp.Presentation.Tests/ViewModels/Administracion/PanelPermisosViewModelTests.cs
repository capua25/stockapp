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
    private static UsuarioDto Dto(int id, RolUsuario rol, bool activo = true) =>
        new(id, $"u{id}", null, rol, activo, DateTime.UtcNow);

    private static (PanelPermisosViewModel panel, UsuariosAdminViewModel padre, Mock<IUsuarioService> svc) Crear()
    {
        var svc = new Mock<IUsuarioService>();
        var confirm = new Mock<StockApp.Presentation.Services.IConfirmacionService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin1", RolUsuario.Admin, null));
        var panel = new PanelPermisosViewModel(svc.Object, confirm.Object);
        var padre = new UsuariosAdminViewModel(svc.Object, confirm.Object, panel, session.Object);
        return (panel, padre, svc);
    }

    private static (PanelPermisosViewModel panel, UsuariosAdminViewModel padre, Mock<IUsuarioService> svc,
        Mock<StockApp.Presentation.Services.IConfirmacionService> confirm) CrearConConfirmacion()
    {
        var svc = new Mock<IUsuarioService>();
        var confirm = new Mock<StockApp.Presentation.Services.IConfirmacionService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin1", RolUsuario.Admin, null));
        var panel = new PanelPermisosViewModel(svc.Object, confirm.Object);
        var padre = new UsuariosAdminViewModel(svc.Object, confirm.Object, panel, session.Object);
        return (panel, padre, svc, confirm);
    }

    /// <summary>Helper central (recomendado por el plan): evita repetir
    /// panel.Grupos.SelectMany(g => g.Items).Single(...) treinta veces.</summary>
    private static ItemPermiso Item(PanelPermisosViewModel panel, string permiso) =>
        panel.Grupos.SelectMany(g => g.Items).Single(i => i.Clave == permiso);

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

        Assert.True(Item(panel, Permisos.VerFinanzas).Seleccionado);
        Assert.True(Item(panel, Permisos.GestionarProductos).Seleccionado);
        Assert.False(Item(panel, Permisos.RegistrarGastos).Seleccionado);
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

        Assert.False(Item(panel, Permisos.VerFinanzas).Seleccionado);
        svc.Verify(s => s.ObtenerPermisosAsync(It.IsAny<int>()), Times.Never);
    }

    // ── Aplanado (Task 3/4/6): 12 checkboxes independientes, sin compuestos ni efectos
    // laterales entre ítems. Los tests que verificaban el comportamiento de los compuestos
    // (GastosYFacturas_AlTildar_EnciendeRegistrarGastosRegistrarPagosYVerFinanzas,
    // GastosYFacturas_AlDestildar_ApagaGastosYPagosPeroNoVerFinanzas,
    // IngresosDeCaja_AlTildar_EnciendeRegistrarIngresosYVerFinanzas,
    // Productos_AlTildar_EnciendeGestionarProductosYRecalcularStockJuntos) se ELIMINARON: ese
    // comportamiento (un checkbox prendía otros permisos por su cuenta) se saca a propósito.
    // La decisión central del refactor es que el Admin ve y concede EXACTAMENTE lo que tilda;
    // la protección contra combinaciones inválidas ya la da
    // UsuarioService.GuardarPermisosAsync validando PermisoDependencias.Requisitos (mergeado
    // en main). Este test nuevo documenta la independencia que las reemplaza.
    [Fact]
    public void Item_TildarUno_NoAfectaAOtrosItems()
    {
        var (panel, _, _) = Crear();

        Item(panel, Permisos.GestionarProductos).Seleccionado = true;

        Assert.False(Item(panel, Permisos.RecalcularStock).Seleccionado);
        Assert.False(Item(panel, Permisos.VerFinanzas).Seleccionado);
        Assert.False(Item(panel, Permisos.RegistrarGastos).Seleccionado);
        Assert.False(Item(panel, Permisos.RegistrarPagos).Seleccionado);
    }

    [Fact]
    public void Grupos_RespetanElOrdenDeDeclaracionDelCatalogo_NoOrdenAlfabetico()
    {
        // "Documentos" va último en el catálogo (Tarea/Reportes lo precede) pero alfabéticamente
        // caería 2do (Catálogo, Documentos, Finanzas, Tareas y reportes) -- si Grupos usara
        // OrderBy alfabético este test lo detecta.
        var (panel, _, _) = Crear();

        Assert.Equal(
            new[] { "Catálogo", "Finanzas", "Tareas y reportes", "Documentos" },
            panel.Grupos.Select(g => g.Nombre));
    }

    [Fact]
    public void Grupos_ItemsRespetanElOrdenDeDeclaracionDentroDelGrupo()
    {
        var (panel, _, _) = Crear();

        var grupoFinanzas = panel.Grupos.Single(g => g.Nombre == "Finanzas");

        Assert.Equal(
            new[]
            {
                Permisos.RegistrarGastos, Permisos.RegistrarPagos, Permisos.RegistrarIngresos,
                Permisos.VerFinanzas, Permisos.GestionarMaestrosFinanzas,
            },
            grupoFinanzas.Items.Select(i => i.Clave));
    }

    [Fact]
    public async Task GuardarAsync_EnviaSoloLosPermisosTildados()
    {
        var (panel, padre, svc) = Crear();
        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        Item(panel, Permisos.VerFinanzas).Seleccionado = true;
        Item(panel, Permisos.GestionarTareas).Seleccionado = true;

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
    //
    // NOTA: CargarAsync_NotificaLasPropiedadesCompuestas_NoSoloLasBase se ELIMINÓ. Ese test
    // cubría un riesgo específico de los checkboxes COMPUESTOS: CargarAsync asignaba las
    // propiedades base directamente (sin pasar por los setters de Productos/GastosYFacturas/
    // IngresosDeCaja), así que hacía falta [NotifyPropertyChangedFor] a mano para que el
    // binding del compuesto se enterara. Sin compuestos, cada ItemPermiso.Seleccionado se
    // asigna directo en CargarAsync y notifica solo (el setter generado por
    // [ObservableProperty] ya dispara PropertyChanged) -- el riesgo que este test vigilaba ya
    // no existe en el diseño nuevo.

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
        Assert.True(Item(panel, Permisos.VerFinanzas).Seleccionado);
        Assert.True(Item(panel, Permisos.GestionarProductos).Seleccionado);

        svc.Setup(s => s.ObtenerPermisosAsync(11))
            .ThrowsAsync(new InvalidOperationException("el servidor no respondió"));
        padre.UsuarioSeleccionado = Dto(11, RolUsuario.Operador);
        await panel._tareaCarga;

        Assert.False(Item(panel, Permisos.VerFinanzas).Seleccionado);
        Assert.False(Item(panel, Permisos.GestionarProductos).Seleccionado);
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
    //
    // La garantía de completitud (¿todo permiso configurable tiene checkbox?) ahora la da
    // CatalogoPermisosPanelTests, un nivel antes (Tasks 3/4/6) -- este test queda para
    // verificar que GuardarAsync efectivamente ENVÍA todo lo tildado, no para detectar
    // checkboxes faltantes.

    [Fact]
    public async Task GuardarAsync_TildarTodosLosCheckboxes_EnviaExactamenteLosPermisosConfigurables()
    {
        var (panel, padre, svc) = Crear();
        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);

        foreach (var item in panel.Grupos.SelectMany(g => g.Items))
            item.Seleccionado = true;

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

        Item(panel, Permisos.VerReportes).Seleccionado = true;

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

        Assert.True(Item(panel, Permisos.GestionarDocumentos).Seleccionado);

        List<string>? enviados = null;
        svc.Setup(s => s.GuardarPermisosAsync(9, It.IsAny<IReadOnlyList<string>>()))
            .Callback<int, IReadOnlyList<string>>((_, permisos) => enviados = permisos.ToList())
            .Returns(Task.CompletedTask);

        await panel.GuardarCommand.ExecuteAsync(null);

        Assert.Contains(Permisos.GestionarDocumentos, enviados ?? new List<string>());
    }

    // ── Feedback faltante (reporte de uso real 2026-08-14): guardar permisos no mostraba
    // NINGÚN mensaje de éxito ni de error — el patrón establecido en toda la app para
    // confirmar una acción puntual es IConfirmacionService.InformarAsync (ver
    // UsuariosAdminViewModel.CambiarContrasenaAsync: "Contraseña actualizada.", el mismo
    // mecanismo usado tanto para éxito como para error en los comandos de esta pantalla).

    [Fact]
    public async Task GuardarAsync_Exitoso_InformaLaConfirmacion()
    {
        var (panel, padre, svc, confirm) = CrearConConfirmacion();
        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        svc.Setup(s => s.GuardarPermisosAsync(9, It.IsAny<IReadOnlyList<string>>()))
            .Returns(Task.CompletedTask);

        await panel.GuardarCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("Permisos guardados."), Times.Once);
    }

    [Fact]
    public async Task GuardarAsync_Falla_InformaElErrorYNoElMensajeDeExito()
    {
        var (panel, padre, svc, confirm) = CrearConConfirmacion();
        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        svc.Setup(s => s.GuardarPermisosAsync(9, It.IsAny<IReadOnlyList<string>>()))
            .ThrowsAsync(new StockApp.Domain.Exceptions.ReglaDeNegocioException("no se pudieron guardar los permisos"));

        await panel.GuardarCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("no se pudieron guardar los permisos"), Times.Once);
        confirm.Verify(c => c.InformarAsync("Permisos guardados."), Times.Never);
    }

    // ── Paso 5 del refactor: aviso NO bloqueante para dependencias BLANDAS
    // (PermisoDependencias.Recomendados). A diferencia de Requisitos (dependencia DURA,
    // validada server-side en UsuarioService.GuardarPermisosAsync), esta combinación es válida
    // y sostiene un rol real -- el aviso es informativo, nunca impide el guardado.

    [Fact]
    public void TildarRegistrarMovimientos_SinGestionarProductos_MuestraLaAdvertencia()
    {
        var (panel, _, _) = Crear();

        Item(panel, Permisos.RegistrarMovimientos).Seleccionado = true;

        Assert.Equal(
            PermisoDependencias.Recomendados[Permisos.RegistrarMovimientos].Mensaje,
            Item(panel, Permisos.RegistrarMovimientos).Advertencia);
    }

    [Fact]
    public void TildarTambienGestionarProductos_OcultaLaAdvertencia()
    {
        var (panel, _, _) = Crear();
        Item(panel, Permisos.RegistrarMovimientos).Seleccionado = true;

        Item(panel, Permisos.GestionarProductos).Seleccionado = true;

        Assert.Null(Item(panel, Permisos.RegistrarMovimientos).Advertencia);
    }

    [Fact]
    public void DestildarGestionarProductosDeNuevo_ReaparecenLaAdvertencia()
    {
        var (panel, _, _) = Crear();
        Item(panel, Permisos.RegistrarMovimientos).Seleccionado = true;
        Item(panel, Permisos.GestionarProductos).Seleccionado = true;

        Item(panel, Permisos.GestionarProductos).Seleccionado = false;

        Assert.Equal(
            PermisoDependencias.Recomendados[Permisos.RegistrarMovimientos].Mensaje,
            Item(panel, Permisos.RegistrarMovimientos).Advertencia);
    }

    [Fact]
    public void PermisoSinRecomendacion_NuncaMuestraAdvertencia()
    {
        var (panel, _, _) = Crear();

        Item(panel, Permisos.VerReportes).Seleccionado = true;

        Assert.Null(Item(panel, Permisos.VerReportes).Advertencia);
    }

    [Fact]
    public async Task GuardarAsync_ConCombinacionAvisada_NoBloqueaElGuardado()
    {
        // El aviso es informativo: RegistrarMovimientos tildado sin GestionarProductos se
        // guarda igual, tal cual lo tildó el Admin.
        var (panel, padre, svc) = Crear();
        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        Item(panel, Permisos.RegistrarMovimientos).Seleccionado = true;

        await panel.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.GuardarPermisosAsync(9,
            It.Is<IReadOnlyList<string>>(l => l.Contains(Permisos.RegistrarMovimientos) && l.Count == 1)),
            Times.Once);
    }

    // ── Bug reportado por uso real (2026-08-16): el panel seguía completamente editable para
    // un usuario dado de baja -- se le podían tildar/destildar permisos y guardar como si
    // estuviera activo. UsuarioService.GuardarPermisosAsync ya rechaza del lado servidor
    // (ReglaDeNegocioException); acá se verifica la barrera de UI: GuardarCommand deshabilitado
    // y un indicador visual ("Solo lectura") que explique por qué, sin ocultar el panel --
    // sigue siendo necesario poder CONSULTAR qué permisos tenía.

    [Fact]
    public async Task UsuarioInactivoSeleccionado_GuardarCommand_NoPuedeEjecutarse()
    {
        // Mock configurado (no unconfigured) a propósito: deja explícito que el test aísla
        // PuedeEditar y no depende del comportamiento por default de ObtenerPermisosAsync sin
        // Setup (lista null vía Moq). Antes del fix 2026-08-20, ese null hacía que CargarAsync
        // terminara en su catch y MensajeError quedara seteado, tapando la razón real que este
        // test quiere aislar -- ya no es el caso (null se trata como colección vacía), pero el
        // Setup explícito se mantiene igual: es más claro que depender del default de Moq.
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(9)).ReturnsAsync(new List<string>());

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador, activo: false);
        await panel._tareaCarga;

        Assert.False(panel.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task UsuarioActivoSeleccionado_GuardarCommand_PuedeEjecutarse()
    {
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(9)).ReturnsAsync(new List<string>());

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador, activo: true);
        await panel._tareaCarga;

        Assert.True(panel.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public void UsuarioInactivoNoAdminSeleccionado_MuestraAvisoSoloLectura()
    {
        var (panel, padre, _) = Crear();

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador, activo: false);

        Assert.True(panel.MostrarAvisoSoloLectura);
    }

    [Fact]
    public void UsuarioActivoSeleccionado_NoMuestraAvisoSoloLectura()
    {
        var (panel, padre, _) = Crear();

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador, activo: true);

        Assert.False(panel.MostrarAvisoSoloLectura);
    }

    [Fact]
    public void AdminSeleccionado_NoMuestraAvisoSoloLectura()
    {
        // El Admin ya tiene su propio aviso ("Acceso total") -- no duplicar el mensaje.
        var (panel, padre, _) = Crear();

        padre.UsuarioSeleccionado = Dto(1, RolUsuario.Admin);

        Assert.False(panel.MostrarAvisoSoloLectura);
    }

    [Fact]
    public void SinUsuarioSeleccionado_NoMuestraAvisoSoloLectura()
    {
        var (panel, _, _) = Crear();

        Assert.False(panel.MostrarAvisoSoloLectura);
    }

    // ── Defensa null en CargarAsync (fix 2026-08-20): ObtenerPermisosAsync no puede devolver
    // null hoy en producción (el contrato real de UsuarioService.ObtenerPermisosAsync siempre
    // da una lista), pero un mock sin Setup sí lo hace (default de Moq para tipos referencia) y
    // eso disparaba un ArgumentNullException real en `new HashSet<string>(permisos)` que
    // terminaba logueado en crash.log vía RefrescoPermisos -- una de las dos fuentes de
    // contaminación del log real durante `dotnet test` (75 de 99 entradas). Tratar null como
    // colección vacía es la defensa correcta en cualquier caso: no hay por qué confiar
    // ciegamente en que el contrato del servicio nunca cambie.
    [Fact]
    public async Task CargarAsync_ObtenerPermisosDevuelveNull_LoTrataComoColeccionVacia_NoLanza()
    {
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(9)).ReturnsAsync((List<string>)null!);

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        var ex = await Record.ExceptionAsync(() => panel._tareaCarga);

        Assert.Null(ex);
        Assert.Null(panel.MensajeError);
        Assert.All(panel.Grupos.SelectMany(g => g.Items), item => Assert.False(item.Seleccionado));
    }
}
