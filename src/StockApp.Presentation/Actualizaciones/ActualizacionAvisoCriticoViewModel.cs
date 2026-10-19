using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Actualizaciones;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// ViewModel de la franja roja fija para una actualización crítica hallada a mitad de sesión.
/// No se puede posponer ni cerrar: el bloqueo duro llega en el próximo arranque.
/// </summary>
public partial class ActualizacionAvisoCriticoViewModel : ViewModelBase
{
    public const string Mensaje =
        "Hay una actualización crítica. Guardá lo que estés haciendo y tocá Actualizar ahora.";

    /// <summary>Versión disponible, si la trae el resultado del chequeo. Puede ser null.</summary>
    [ObservableProperty]
    private string? _version;

    /// <summary>True mientras se aplica la actualización; la View deshabilita el botón.
    /// La asigna <see cref="ViewModels.ShellViewModel"/>, que orquesta el flujo real.</summary>
    [ObservableProperty]
    private bool _operacionEnCurso;

    /// <summary>Se dispara cuando el usuario toca "Actualizar ahora".</summary>
    public event Action? ActualizarAhoraSolicitado;

    public ActualizacionAvisoCriticoViewModel(AccionUx accion)
    {
        _version = accion.Version;
    }

    [RelayCommand]
    private void ActualizarAhora()
        => ActualizarAhoraSolicitado?.Invoke();
}
