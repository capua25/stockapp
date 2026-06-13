using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Actualizaciones;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// ViewModel del modal posponible de actualización (severity <c>important</c>).
/// Aparece cada arranque hasta que el usuario actualice. La app sigue siendo usable detrás.
/// </summary>
public partial class ActualizacionModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _textoMarkdown;

    [ObservableProperty]
    private bool _esPosponible;

    /// <summary>Se dispara cuando el usuario elige actualizar ahora.</summary>
    public event Action? ActualizarAhoraSolicitado;

    /// <summary>Se dispara cuando el usuario pospone la actualización.</summary>
    public event Action? PosponerSolicitado;

    public ActualizacionModalViewModel(AccionUx accion)
    {
        _textoMarkdown = accion.TextoMarkdown;
        _esPosponible  = accion.Posponible;
    }

    /// <summary>El usuario decide actualizar ahora: descarga (ya hecha) y reinicia.</summary>
    [RelayCommand]
    private void ActualizarAhora()
        => ActualizarAhoraSolicitado?.Invoke();

    /// <summary>El usuario pospone: se cierra el modal, reaparecerá en el próximo arranque.</summary>
    [RelayCommand]
    private void Posponer()
        => PosponerSolicitado?.Invoke();
}
