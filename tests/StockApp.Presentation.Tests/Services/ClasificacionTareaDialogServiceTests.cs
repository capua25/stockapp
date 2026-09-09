using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Tareas;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Mismo criterio defensivo que ConfirmacionServiceTests: sin Avalonia.Application.Current
/// inicializado (tests headless de este proyecto, sin [AvaloniaFact]), PedirClasificacionAsync
/// debe resolver sin excepción devolviendo null (equivale a cancelar), no intentar abrir una
/// ventana real.
/// </summary>
public class ClasificacionTareaDialogServiceTests
{
    [Fact]
    public async Task PedirClasificacionAsync_SinAppAvalonia_DevuelveNull()
    {
        var svc = new ClasificacionTareaDialogService(
            Mock.Of<IZonaService>(), Mock.Of<IDimensionTematicaService>(),
            Mock.Of<IOrganismoResponsableService>(), Mock.Of<IOrigenFinanciamientoService>(),
            Mock.Of<IDocumentoAdministrativoService>(), Mock.Of<IConfirmacionService>());

        var resultado = await svc.PedirClasificacionAsync(new DatosClasificacionTarea(null, null, null, null, null));

        Assert.Null(resultado);
    }
}
