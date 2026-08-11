using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Pantalla de administración de usuarios (spec 2026-08-10): ABM completo — listar, alta, baja
/// lógica, cambio de rol, cambio de contraseña — que hasta esta task no existía en el desktop
/// pese a que el backend ya lo soportaba desde Fase 2b. Layout de dos columnas: esta clase
/// gobierna la izquierda (lista + alta); el panel de permisos de la derecha es
/// PanelPermisosViewModel (Task 13), que lee UsuarioSeleccionado de esta misma instancia.
/// </summary>
public partial class UsuariosAdminViewModel : ViewModelBase
{
    private readonly IUsuarioService _usuarios;
    private readonly IConfirmacionService _confirmacion;
    private readonly ICurrentSession _session;

    public ObservableCollection<UsuarioDto> Items { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    [NotifyCanExecuteChangedFor(nameof(CambiarRolCommand))]
    [NotifyCanExecuteChangedFor(nameof(CambiarContrasenaCommand))]
    [NotifyPropertyChangedFor(nameof(EsAdminSeleccionado))]
    private UsuarioDto? _usuarioSeleccionado;

    /// <summary>Gatea el panel de permisos de la Task 13: "Acceso total" deshabilitado para Admin.</summary>
    public bool EsAdminSeleccionado => UsuarioSeleccionado?.Rol == RolUsuario.Admin;

    [ObservableProperty] private string _nuevoNombreUsuario = string.Empty;
    [ObservableProperty] private string? _nuevoNombreCompleto;
    [ObservableProperty] private string _nuevaContrasenaPlan = string.Empty;
    [ObservableProperty] private RolUsuario _nuevoRol = RolUsuario.Operador;
    [ObservableProperty] private string? _mensajeError;
    [ObservableProperty] private string _nuevaContrasenaParaSeleccionado = string.Empty;

    /// <summary>Fuente del ComboBox de rol en el alta (View, Step 5) — evita hardcodear los
    /// dos valores del enum en el XAML.</summary>
    public IReadOnlyList<RolUsuario> RolesDisponibles { get; } = Enum.GetValues<RolUsuario>();

    /// <summary>Panel de permisos de la columna derecha (Task 13). Recibido por DI —
    /// PanelPermisosViewModel se registra AddTransient sin depender de este tipo en su propio
    /// constructor (ver Decisión de diseño 2 de la Task 13: ViewLocator exige Views sin
    /// argumentos, así que la composición vive enteramente acá, no en el code-behind).</summary>
    public PanelPermisosViewModel PanelPermisos { get; }

    public UsuariosAdminViewModel(
        IUsuarioService usuarios, IConfirmacionService confirmacion, PanelPermisosViewModel panelPermisos,
        ICurrentSession session)
    {
        _usuarios = usuarios;
        _confirmacion = confirmacion;
        PanelPermisos = panelPermisos;
        PanelPermisos.Conectar(this);
        _session = session;
    }

    public async Task CargarAsync()
    {
        var lista = await _usuarios.ListarAsync();
        Items.Clear();
        foreach (var u in lista)
            Items.Add(u);
    }

    [RelayCommand]
    private async Task AltaAsync()
    {
        MensajeError = null;
        try
        {
            await _usuarios.AltaUsuarioAsync(NuevoNombreUsuario, NuevoNombreCompleto, NuevaContrasenaPlan, NuevoRol);
            NuevoNombreUsuario = string.Empty;
            NuevoNombreCompleto = null;
            NuevaContrasenaPlan = string.Empty;
            NuevoRol = RolUsuario.Operador;
            await CargarAsync();
        }
        // Fix (Task 15): sumamos UnauthorizedAccessException — si el permiso GestionarUsuarios
        // se revoca a mitad de sesión, el manejo central del 403 (AuthTokenHandler/App.axaml.cs)
        // ya avisa y refresca el menú, pero la excepción sigue subiendo hasta acá. Sin este
        // catch, el comando explota mudo: crash.log y el botón "sin efecto" para quien mira.
        catch (Exception ex) when (ex is ReglaDeNegocioException or ArgumentException or UnauthorizedAccessException)
        {
            MensajeError = ex.Message;
        }
    }

    private bool PuedeOperarSobreSeleccionado() => UsuarioSeleccionado is not null;

    [RelayCommand(CanExecute = nameof(PuedeOperarSobreSeleccionado))]
    private async Task BajaAsync()
    {
        if (UsuarioSeleccionado is null) return;

        MensajeError = null;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja a \"{UsuarioSeleccionado.NombreUsuario}\"?");
        if (!confirmar) return;

        try
        {
            await _usuarios.BajaLogicaAsync(UsuarioSeleccionado.Id);
            await CargarAsync();
        }
        // Fix (Task 15, Round 1): UnauthorizedAccessException se atrapa APARTE, sin mostrar
        // ex.Message — un 403 ya dispara el aviso central en App.axaml.cs (mensaje propio +
        // refresco de permisos). Mostrar acá el detalle crudo del servidor sería un segundo
        // diálogo para el mismo evento. El catch solo existe para que la excepción no escape
        // del comando (AsyncRelayCommand no debe explotar mudo).
        catch (UnauthorizedAccessException)
        {
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeOperarSobreSeleccionado))]
    private async Task CambiarRolAsync(RolUsuario nuevoRol)
    {
        if (UsuarioSeleccionado is null) return;

        MensajeError = null;

        try
        {
            await _usuarios.CambiarRolAsync(UsuarioSeleccionado.Id, nuevoRol);
            await CargarAsync();
        }
        // Fix (review Task 12): faltaba EntidadNoEncontradaException, que
        // UsuarioService.CambiarRolAsync SÍ puede lanzar (usuario dado de baja por otro Admin
        // entremedio) — sin este catch la excepción escapaba del AsyncRelayCommand y el botón
        // quedaba "roto" sin feedback, mismo mecanismo que ya documentan GastosViewModel.AnularAsync
        // e IngresoPorFacturaViewModel.GuardarInternoAsync.
        // Fix (Task 15, Round 1): UnauthorizedAccessException se atrapa APARTE, sin mostrar
        // ex.Message — mismo motivo que en BajaAsync: el aviso central del 403 ya avisó, este
        // catch solo evita que la excepción escape.
        catch (UnauthorizedAccessException)
        {
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or ArgumentException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeOperarSobreSeleccionado))]
    private async Task CambiarContrasenaAsync()
    {
        if (UsuarioSeleccionado is null) return;

        MensajeError = null;

        // Bloqueo explícito (Round 2, review Task 12): esta pantalla es un reset
        // administrativo — SIEMPRE manda contrasenaActualPlan: null (ver comentario más abajo).
        // Si el seleccionado sos vos mismo, UsuarioService.CambiarContrasenaAsync (Fix 7, §5.1)
        // SIEMPRE rechaza con UnauthorizedAccessException porque el auto-cambio exige verificar
        // la contraseña actual, y acá no hay ningún campo para proveerla. Se corta ANTES de
        // llamar al servicio (mismo criterio de "no dejar que explote" que BajaLogicaAsync usa
        // para la auto-baja), con un mensaje propio que explica el motivo y la salida real —
        // el mensaje de UnauthorizedAccessException del servicio ("confirmá tu contraseña
        // actual") sería un callejón sin salida acá, porque este formulario no tiene ese campo.
        if (UsuarioSeleccionado.Id == _session.UsuarioActual?.Id)
        {
            await _confirmacion.InformarAsync(
                "No podés cambiar tu propia contraseña desde esta pantalla: acá el reset no " +
                "pide tu contraseña actual, y el cambio propio sí la exige por seguridad. " +
                "Pedile a otro Admin que te la cambie desde esta misma pantalla.");
            return;
        }

        // Confirmación (decisión de review, Task 12): resetear la contraseña de otro usuario
        // le corta el acceso con su clave actual sin aviso previo — mismo mecanismo de
        // PreguntarAsync que ya usa BajaAsync más arriba en esta clase.
        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma cambiar la contraseña de \"{UsuarioSeleccionado.NombreUsuario}\"? " +
            "La persona no va a poder volver a entrar con su contraseña actual.");
        if (!confirmar) return;

        try
        {
            // Reset administrativo (spec Auth §5.1): Admin cambia la de otro sin requerir la
            // contraseña actual — tercer argumento null a propósito, mismo criterio que ya
            // implementa UsuarioService.CambiarContrasenaAsync.
            await _usuarios.CambiarContrasenaAsync(UsuarioSeleccionado.Id, NuevaContrasenaParaSeleccionado, null);
            NuevaContrasenaParaSeleccionado = string.Empty;
            await _confirmacion.InformarAsync("Contraseña actualizada.");
        }
        // Fix (review Task 12, Round 1): faltaba EntidadNoEncontradaException, mismo motivo
        // que en CambiarRolAsync — UsuarioService.CambiarContrasenaAsync también la lanza.
        // Fix (review Task 12, Round 2): sumamos UnauthorizedAccessException como defensa en
        // profundidad — el bloqueo explícito de arriba cubre el camino conocido (auto-cambio),
        // pero si mañana aparece otro camino que la dispare (ej. el permiso se revoca a mitad
        // de sesión), que se muestre como diálogo y no como una falla muda en crash.log.
        // Fix (Task 15, Round 1): esa "defensa en profundidad" original mostraba ex.Message —
        // correcto ANTES de esta task, cuando no existía otro aviso. Ahora que el 403 dispara
        // el aviso central en App.axaml.cs, mostrar acá el mensaje crudo del servidor duplica
        // el aviso para el mismo evento. Se separa en su propio catch, sin diálogo local: el
        // objetivo pasa a ser solamente que la excepción no escape.
        catch (UnauthorizedAccessException)
        {
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or ArgumentException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
