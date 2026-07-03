using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Actualizaciones;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// ViewModel del banner discreto de actualización (severity <c>normal</c>).
/// No bloquea la UI; el usuario puede ignorarlo hasta el próximo reinicio voluntario.
/// </summary>
public partial class ActualizacionBannerViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _textoMarkdown;

    [ObservableProperty]
    private bool _esPosponible;

    /// <summary>Versión disponible, si la trae el resultado del chequeo. Puede ser null.</summary>
    [ObservableProperty]
    private string? _version;

    /// <summary>Título de la franja, ej. "Nueva versión v0.1.2 disponible".</summary>
    public string Titulo =>
        string.IsNullOrWhiteSpace(Version)
            ? "Nueva versión disponible"
            : $"Nueva versión v{Version} disponible";

    /// <summary>Notas de la release ya limpias (sin severity ni instrucciones internas).
    /// La franja discreta no las muestra, pero quedan disponibles para quien las necesite.</summary>
    public string NotasLimpias => FormateadorNotasActualizacion.Limpiar(TextoMarkdown);

    /// <summary>Se dispara cuando el usuario confirma que actualizará al reiniciar.</summary>
    public event Action? ActualizarAlReiniciarSolicitado;

    /// <summary>Se dispara cuando el usuario pospone el banner.</summary>
    public event Action? PosponerSolicitado;

    public ActualizacionBannerViewModel(AccionUx accion)
    {
        _textoMarkdown = accion.TextoMarkdown;
        _esPosponible  = accion.Posponible;
        _version       = accion.Version;
    }

    /// <summary>El usuario acepta que la app se actualice al próximo reinicio.</summary>
    [RelayCommand]
    private void ActualizarAlReiniciar()
        => ActualizarAlReiniciarSolicitado?.Invoke();

    /// <summary>El usuario pospone el banner: se cierra, reaparecerá en el próximo arranque.</summary>
    [RelayCommand]
    private void Posponer()
        => PosponerSolicitado?.Invoke();
}
