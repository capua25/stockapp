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

    /// <summary>Se dispara cuando el usuario confirma que actualizará al reiniciar.</summary>
    public event Action? ActualizarAlReiniciarSolicitado;

    public ActualizacionBannerViewModel(AccionUx accion)
    {
        _textoMarkdown = accion.TextoMarkdown;
        _esPosponible  = accion.Posponible;
    }

    /// <summary>El usuario acepta que la app se actualice al próximo reinicio.</summary>
    [RelayCommand]
    private void ActualizarAlReiniciar()
        => ActualizarAlReiniciarSolicitado?.Invoke();
}
