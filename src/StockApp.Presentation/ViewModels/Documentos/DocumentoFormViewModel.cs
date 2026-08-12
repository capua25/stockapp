using System.Threading.Tasks;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.ViewModels.Documentos;

/// <summary>
/// Stub temporal (Task 19): DocumentoListViewModel.Nuevo/VerDetalle navegan a este ViewModel,
/// pero su implementación real (alta/edición/detalle, con Anular vía PedirTextoAsync) es la
/// Task 20 del plan. Existe acá únicamente para que Task 19 compile de forma aislada -- la
/// Task 20 reemplaza este archivo por completo.
/// </summary>
public partial class DocumentoFormViewModel : ViewModelBase
{
    public void CargarParaCrear()
    {
    }

    public Task CargarParaVerAsync(DocumentoAdministrativo documento) => Task.CompletedTask;
}
