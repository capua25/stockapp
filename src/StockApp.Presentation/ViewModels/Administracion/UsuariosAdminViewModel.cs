using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;
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

    public UsuariosAdminViewModel(IUsuarioService usuarios, IConfirmacionService confirmacion)
    {
        _usuarios = usuarios;
        _confirmacion = confirmacion;
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
        catch (Exception ex) when (ex is ReglaDeNegocioException or ArgumentException)
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
        // Fix (review Task 12): faltaba EntidadNoEncontradaException, mismo motivo que en
        // CambiarRolAsync — UsuarioService.CambiarContrasenaAsync también la lanza.
        catch (Exception ex) when (ex is ReglaDeNegocioException or ArgumentException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
