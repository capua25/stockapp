using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Licenciamiento;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Flujo "No puedo entrar / resetear Admin" desde el login. Paso 1: pedir un desafío (muestra
/// desafío + código de máquina para copiar). Paso 2: pegar el token firmado + nueva contraseña
/// y aplicar el reset. <see cref="Volver"/> regresa al login.
/// </summary>
public partial class ResetAdminViewModel : ViewModelBase
{
    private readonly IResetAdminService _reset;

    /// <summary>El usuario pidió volver al login.</summary>
    public event Action? Volver;

    /// <summary>
    /// Comando manual (no [RelayCommand]) para garantizar el nombre exacto "VolverCommand":
    /// un método llamado "Volver" colisionaría con el evento del mismo nombre, y renombrarlo
    /// con guion bajo o sufijo haría que el generador produzca un nombre distinto de comando.
    /// </summary>
    public IRelayCommand VolverCommand { get; }

    [ObservableProperty]
    private string _codigoMaquina = "";

    [ObservableProperty]
    private string _desafio = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetearCommand))]
    private string _tokenPegado = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetearCommand))]
    private string _nuevaContrasena = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    private bool _completado;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetearCommand))]
    private bool _operacionEnCurso;

    public ResetAdminViewModel(IResetAdminService reset)
    {
        _reset = reset;
        VolverCommand = new RelayCommand(() => Volver?.Invoke());
    }

    [RelayCommand]
    private async Task PedirDesafioAsync()
    {
        MensajeError = null;
        try
        {
            var dto = await _reset.SolicitarDesafioAsync();
            Desafio = dto.Desafio;
            CodigoMaquina = dto.CodigoMaquina;
        }
        catch (ServidorNoDisponibleException ex)
        {
            MensajeError = ex.Message;
        }
    }

    private bool PuedeResetear()
        => !string.IsNullOrWhiteSpace(TokenPegado)
        && !string.IsNullOrWhiteSpace(NuevaContrasena)
        && !OperacionEnCurso;

    [RelayCommand(CanExecute = nameof(PuedeResetear))]
    private async Task ResetearAsync()
    {
        OperacionEnCurso = true;
        MensajeError = null;
        try
        {
            var resultado = await _reset.ResetearAsync(TokenPegado.Trim(), NuevaContrasena);
            if (resultado.Exito)
                Completado = true;
            else
                MensajeError = resultado.Motivo ?? "No se pudo resetear el Admin.";
        }
        catch (ServidorNoDisponibleException ex)
        {
            MensajeError = ex.Message;
        }
        finally
        {
            OperacionEnCurso = false;
        }
    }
}
