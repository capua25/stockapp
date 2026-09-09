using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Tareas;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Tareas;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de IClasificacionTareaDialogService. Mismo mecanismo defensivo que
/// ConfirmacionService (Application.Current/MainWindow pueden faltar en tests headless) — a
/// diferencia de ConfirmacionService, acá el panel se construye a mano con los servicios de
/// catálogo (no se resuelve por contenedor DI: mismo criterio que ConfirmacionService
/// "new"-ea sus diálogos en vez de pedirlos al ServiceProvider).
/// </summary>
public class ClasificacionTareaDialogService : IClasificacionTareaDialogService
{
    private readonly IZonaService _zonasService;
    private readonly IDimensionTematicaService _dimensionesService;
    private readonly IOrganismoResponsableService _organismosService;
    private readonly IOrigenFinanciamientoService _origenesService;
    private readonly IDocumentoAdministrativoService _documentosService;
    private readonly IConfirmacionService _confirmacion;

    public ClasificacionTareaDialogService(
        IZonaService zonasService, IDimensionTematicaService dimensionesService,
        IOrganismoResponsableService organismosService, IOrigenFinanciamientoService origenesService,
        IDocumentoAdministrativoService documentosService, IConfirmacionService confirmacion)
    {
        _zonasService = zonasService;
        _dimensionesService = dimensionesService;
        _organismosService = organismosService;
        _origenesService = origenesService;
        _documentosService = documentosService;
        _confirmacion = confirmacion;
    }

    public Task<DatosClasificacionTarea?> PedirClasificacionAsync(DatosClasificacionTarea actual)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<DatosClasificacionTarea?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => MostrarDialogoAsync(actual));
    }

    private async Task<DatosClasificacionTarea?> MostrarDialogoAsync(DatosClasificacionTarea actual)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var owner = lifetime?.MainWindow;
        if (owner is null) return null;

        var panel = new ClasificacionTareaPanelViewModel(
            _zonasService, _dimensionesService, _organismosService, _origenesService, _documentosService,
            _confirmacion);
        await panel.InicializarAsync(actual);

        var vm = new ReclasificarTareaDialogViewModel(panel);
        var dialog = new ReclasificarTareaDialog { DataContext = vm };
        return await dialog.ShowDialog<DatosClasificacionTarea?>(owner);
    }
}
