using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Exceptions;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Pantalla de bloqueo pre-login: muestra el código de máquina del servidor (para copiar y
/// pasárselo al desarrollador) y un campo para pegar la licencia y activarla. Al activar OK,
/// dispara <see cref="LicenciaActivada"/> — el Shell pasa al login.
/// </summary>
public partial class BloqueoLicenciaViewModel : ViewModelBase
{
    private readonly ILicenciaService _licencia;

    /// <summary>La activación fue exitosa; el Shell debe navegar al login.</summary>
    public event Action? LicenciaActivada;

    /// <summary>
    /// Fix (IMPORTANT, re-review final E1): antes de este fix, esta pantalla era un callejón
    /// sin salida — sin licencia activa no había forma de autenticarse ni de llegar a los
    /// backups, aunque /auth/login y /backups ya estuvieran exentos del bloqueo del lado
    /// servidor. El admin pide entrar en modo acotado (solo Mantenimiento/backups); el Shell
    /// lo cablea a <see cref="ShellViewModel.MostrarLoginAccesoLimitado"/>.
    /// </summary>
    public event Action? IngresoLimitadoSolicitado;

    [ObservableProperty]
    private string _codigoMaquina = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivarCommand))]
    private string _licenciaPegada = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivarCommand))]
    private bool _operacionEnCurso;

    public BloqueoLicenciaViewModel(ILicenciaService licencia) => _licencia = licencia;

    /// <summary>Carga el código de máquina desde la API (se llama al mostrar la pantalla).</summary>
    public async Task CargarEstadoAsync()
    {
        try
        {
            var estado = await _licencia.ObtenerEstadoAsync();
            CodigoMaquina = estado.CodigoMaquina;
        }
        catch (ServidorNoDisponibleException ex)
        {
            MensajeError = ex.Message;
        }
    }

    private bool PuedeActivar()
        => !string.IsNullOrWhiteSpace(LicenciaPegada) && !OperacionEnCurso;

    [RelayCommand(CanExecute = nameof(PuedeActivar))]
    private async Task ActivarAsync()
    {
        OperacionEnCurso = true;
        MensajeError = null;
        try
        {
            var resultado = await _licencia.ActivarAsync(LicenciaPegada.Trim());
            if (resultado.Exito)
                LicenciaActivada?.Invoke();
            else
                MensajeError = resultado.Motivo ?? "No se pudo activar la licencia.";
        }
        catch (ServidorNoDisponibleException ex)
        {
            MensajeError = ex.Message;
        }
        catch (ReglaDeNegocioException ex)
        {
            // Incluye el 429 del rate limiter de intentos de activación.
            MensajeError = ex.Message;
        }
        finally
        {
            OperacionEnCurso = false;
        }
    }

    /// <summary>
    /// Único camino a los backups con licencia vencida (FIX 1, re-review final E1): dispara
    /// <see cref="IngresoLimitadoSolicitado"/> para que el Shell muestre un login que, al
    /// autenticar, navega a Mantenimiento en modo acotado — no al shell completo.
    /// </summary>
    [RelayCommand]
    private void IrALoginAccesoLimitado() => IngresoLimitadoSolicitado?.Invoke();
}
