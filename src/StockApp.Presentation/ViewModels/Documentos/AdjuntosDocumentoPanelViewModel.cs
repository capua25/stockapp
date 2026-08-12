using System.Collections.ObjectModel;
using System.Threading.Tasks;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Documentos;

/// <summary>
/// Stub temporal (Task 20): DocumentoFormViewModel se inyecta este panel por constructor y lo
/// inicializa en CargarParaVerAsync, pero su implementación real (Items, PuedeAgregar/
/// PuedeQuitar, Agregar/Ver/Quitar) es la Task 21 del plan. Existe acá únicamente para que
/// Task 20 compile de forma aislada -- la Task 21 reemplaza este archivo por completo.
/// </summary>
public partial class AdjuntosDocumentoPanelViewModel : ViewModelBase
{
    public ObservableCollection<AdjuntoDocumentoDto> Items { get; } = new();

    public AdjuntosDocumentoPanelViewModel(
        IAdjuntoDocumentoService adjuntos,
        IServicioSeleccionArchivo seleccion,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion,
        ICurrentSession session)
    {
    }

    public Task InicializarAsync(int documentoId, bool documentoActivo) => Task.CompletedTask;
}
