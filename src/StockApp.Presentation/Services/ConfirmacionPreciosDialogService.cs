using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Dialogs;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>Implementación real: mismo mecanismo defensivo que RecalcularStockDialogService.</summary>
public class ConfirmacionPreciosDialogService : IConfirmacionPreciosDialogService
{
    public Task<IReadOnlyList<bool>?> ConfirmarCambiosAsync(IReadOnlyList<ItemConfirmacionPrecioVm> cambios)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<IReadOnlyList<bool>?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => MostrarAsync(cambios));
    }

    private static async Task<IReadOnlyList<bool>?> MostrarAsync(IReadOnlyList<ItemConfirmacionPrecioVm> cambios)
    {
        var owner = (AvaloniaApp.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
            return null;

        return await new ConfirmacionPreciosDialog(cambios).ShowDialog<IReadOnlyList<bool>?>(owner);
    }
}
