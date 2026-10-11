using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Presentation.Views.Dialogs;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>Implementación real: mismo mecanismo defensivo que RecalcularStockDialogService.</summary>
public class AltaProductoRapidaDialogService : IAltaProductoRapidaDialogService
{
    public Task<ProductoNuevoDto?> PedirProductoNuevoAsync(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<ProductoNuevoDto?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => MostrarAsync(categorias, unidades));
    }

    private static async Task<ProductoNuevoDto?> MostrarAsync(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades)
    {
        var owner = (AvaloniaApp.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
            return null;

        return await new AltaProductoRapidaDialog(categorias, unidades).ShowDialog<ProductoNuevoDto?>(owner);
    }
}
