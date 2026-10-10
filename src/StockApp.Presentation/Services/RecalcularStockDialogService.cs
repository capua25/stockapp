using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StockApp.Application.Catalogo;
using StockApp.Presentation.Views.Dialogs;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de IRecalcularStockDialogService. Mismo mecanismo defensivo que
/// ClasificacionTareaDialogService (Application.Current/MainWindow pueden faltar en tests headless).
/// </summary>
public class RecalcularStockDialogService : IRecalcularStockDialogService
{
    public Task<ProductoDto?> ElegirProductoAsync(
        Func<string?, CancellationToken, Task<IEnumerable<object>>> buscar, Func<bool> sinPermiso)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<ProductoDto?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => MostrarDialogoAsync(buscar, sinPermiso));
    }

    private static async Task<ProductoDto?> MostrarDialogoAsync(
        Func<string?, CancellationToken, Task<IEnumerable<object>>> buscar, Func<bool> sinPermiso)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var owner = lifetime?.MainWindow;
        if (owner is null) return null;

        return await new RecalcularStockDialog(buscar, sinPermiso).ShowDialog<ProductoDto?>(owner);
    }
}
