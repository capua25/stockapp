using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StockApp.Application.Documentos;
using StockApp.Application.Proyectos;
using StockApp.Application.Tareas;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Tareas;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de IClasificacionTareaDialogService. Mismo mecanismo defensivo que
/// ConfirmacionService (Application.Current/MainWindow pueden faltar en tests headless) — a
/// diferencia de ConfirmacionService, acá el panel se construye a mano con los servicios de
/// proyectos y documentos (no se resuelve por contenedor DI: mismo criterio que ConfirmacionService
/// "new"-ea sus diálogos en vez de pedirlos al ServiceProvider).
/// </summary>
public class ClasificacionTareaDialogService : IClasificacionTareaDialogService
{
    private readonly IProyectoService _proyectosService;
    private readonly IDocumentoAdministrativoService _documentosService;
    private readonly IConfirmacionService _confirmacion;

    public ClasificacionTareaDialogService(
        IProyectoService proyectosService, IDocumentoAdministrativoService documentosService,
        IConfirmacionService confirmacion)
    {
        _proyectosService = proyectosService;
        _documentosService = documentosService;
        _confirmacion = confirmacion;
    }

    public Task<DatosClasificacionTarea?> PedirClasificacionAsync(
        DatosClasificacionTarea actual, string? tituloProyectoActual = null)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<DatosClasificacionTarea?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => MostrarDialogoAsync(actual, tituloProyectoActual));
    }

    private async Task<DatosClasificacionTarea?> MostrarDialogoAsync(
        DatosClasificacionTarea actual, string? tituloProyectoActual)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var owner = lifetime?.MainWindow;
        if (owner is null) return null;

        var panel = new ClasificacionTareaPanelViewModel(_proyectosService, _documentosService, _confirmacion);
        await panel.InicializarAsync(actual, tituloProyectoActual);

        var vm = new ReclasificarTareaDialogViewModel(panel);
        var dialog = new ReclasificarTareaDialog { DataContext = vm };
        return await dialog.ShowDialog<DatosClasificacionTarea?>(owner);
    }
}
