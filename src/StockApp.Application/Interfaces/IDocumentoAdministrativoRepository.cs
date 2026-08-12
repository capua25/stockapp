using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Interfaces;

public interface IDocumentoAdministrativoRepository
{
    Task<int> AgregarAsync(DocumentoAdministrativo documento);
    Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro);   // WHERE Estado IN (Pendiente, EnProceso)
    Task<IReadOnlyList<DocumentoAdministrativo>> ListarCerradosAsync(FiltroDocumentos filtro);  // WHERE Estado IN (Finalizado, Anulado)
    Task ActualizarAsync(DocumentoAdministrativo documento);
    Task<bool> ExisteNumeroAsync(TipoDocumento tipo, int anio, string numero, int? excluyendoId = null);
}
