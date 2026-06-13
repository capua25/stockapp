using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Actualizaciones;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// ViewModel del overlay de bloqueo crítico (severity <c>critical</c>).
/// Cubre dos submodos:
/// <list type="bullet">
///   <item><see cref="ModoUx.BloqueoCritico"/>: descarga OK, el usuario debe aplicar y reiniciar.</item>
///   <item><see cref="ModoUx.ModoDegradado"/>: descarga falló; app usable con banner rojo permanente.</item>
/// </list>
/// No tiene opción "Posponer".
/// </summary>
public partial class ActualizacionBloqueoViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _textoMarkdown;

    /// <summary>
    /// Siempre false en bloqueo crítico y modo degradado (no se puede posponer).
    /// </summary>
    [ObservableProperty]
    private bool _esPosponible;

    /// <summary>
    /// True cuando el modo es <see cref="ModoUx.ModoDegradado"/> (descarga fallida).
    /// La View usa esta propiedad para mostrar el banner rojo permanente no-cerrable.
    /// </summary>
    [ObservableProperty]
    private bool _esModoDegradado;

    /// <summary>Se dispara cuando el usuario confirma aplicar el update y reiniciar.</summary>
    public event Action? AplicarYReiniciarSolicitado;

    public ActualizacionBloqueoViewModel(AccionUx accion)
    {
        _textoMarkdown  = accion.TextoMarkdown;
        _esPosponible   = accion.Posponible;   // siempre false para critical
        _esModoDegradado = accion.Modo == ModoUx.ModoDegradado;
    }

    /// <summary>
    /// El usuario acepta aplicar la actualización ya descargada y reiniciar la app.
    /// Solo disponible en <see cref="ModoUx.BloqueoCritico"/> (no en modo degradado).
    /// </summary>
    [RelayCommand]
    private void AplicarYReiniciar()
        => AplicarYReiniciarSolicitado?.Invoke();
}
