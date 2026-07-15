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
}
