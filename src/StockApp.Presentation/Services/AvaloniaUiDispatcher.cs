using System;
using Avalonia.Threading;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de <see cref="IUiDispatcher"/> que marshalea al hilo de UI de Avalonia.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action accion) => Dispatcher.UIThread.Post(accion);
}
