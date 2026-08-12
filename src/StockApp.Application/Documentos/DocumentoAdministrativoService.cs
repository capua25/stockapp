using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Documentos;

/// <summary>
/// Servicio de documentos administrativos. Patrón: auth → validación → mutación vía la
/// entidad (la máquina de estados vive en DocumentoAdministrativo.CambiarEstado) →
/// FechaCierre sellada/limpiada a mano cuando corresponde (D8, el dominio no toca fechas) →
/// persistencia → auditoría. Mismo orden que TareaService. IniciarProceso/VolverAPendiente/
/// Finalizar se implementan en Task 8; AgregarNota en Task 9; Anular/Reabrir en Task 10
/// (agregan el chequeo de Permisos.AdministrarDocumentos); Editar en Task 11.
/// </summary>
public class DocumentoAdministrativoService : IDocumentoAdministrativoService
{
    private readonly IDocumentoAdministrativoRepository _repo;
    private readonly ICurrentSession                    _session;
    private readonly IAuthorizationService               _auth;
    private readonly IAuditLogger                        _audit;

    public DocumentoAdministrativoService(
        IDocumentoAdministrativoRepository repo, ICurrentSession session,
        IAuthorizationService auth, IAuditLogger audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> RegistrarAsync(DocumentoAdministrativo documento)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        if (string.IsNullOrWhiteSpace(documento.Numero))
            throw new ArgumentException("El número del documento es obligatorio.", nameof(documento.Numero));
        if (string.IsNullOrWhiteSpace(documento.Descripcion))
            throw new ArgumentException("La descripción del documento es obligatoria.", nameof(documento.Descripcion));

        if (await _repo.ExisteNumeroAsync(documento.Tipo, documento.Anio, documento.Numero, null))
            throw new ReglaDeNegocioException(
                $"Ya existe un {documento.Tipo} {documento.Numero}/{documento.Anio}.");

        documento.Estado                 = EstadoDocumento.Pendiente;
        documento.RegistradoPorUsuarioId = _session.UsuarioActual!.Id;
        documento.FechaRegistro          = DateTime.UtcNow;
        documento.FechaCierre            = null;

        documento.AgregarEvento(
            _session.UsuarioActual!.Id,
            $"Alta del documento — {documento.Tipo} {documento.Numero}/{documento.Anio}.",
            esAutomatico: true);

        var id = await _repo.AgregarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaDocumentoAdministrativo, "DocumentoAdministrativo", id,
            $"{documento.Tipo} {documento.Numero}/{documento.Anio}");

        return id;
    }

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        // El filtrado Pendiente/EnProceso ya viene resuelto por el repositorio (WHERE Estado IN
        // (...), Task 5) — el servicio no vuelve a filtrar por EsActivo en memoria.
        return _repo.ListarActivosAsync(filtro);
    }

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarHistorialAsync(FiltroDocumentos filtro)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        // D9: el año es obligatorio en el historial — es lo que sostiene la decisión de no
        // paginar. Ausente es un request mal formado (400), no un conflicto de negocio (409).
        if (filtro.Anio is null)
            throw new ArgumentException("El año es obligatorio para consultar el historial.", nameof(filtro));

        // Mismo criterio que ListarActivosAsync: el filtrado Finalizado/Anulado va en el SQL
        // (WHERE Estado IN (...), Task 5), no en memoria.
        return _repo.ListarCerradosAsync(filtro);
    }

    public async Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);
        return await _repo.ObtenerPorIdAsync(id);
    }

    public Task IniciarProcesoAsync(int id) => throw new NotImplementedException();     // Task 8
    public Task VolverAPendienteAsync(int id) => throw new NotImplementedException();   // Task 8
    public Task FinalizarAsync(int id) => throw new NotImplementedException();          // Task 8
    public Task AgregarNotaAsync(int id, string texto) => throw new NotImplementedException(); // Task 9
    public Task AnularAsync(int id, string motivo) => throw new NotImplementedException();     // Task 10
    public Task ReabrirAsync(int id, string motivo) => throw new NotImplementedException();    // Task 10
    public Task EditarAsync(int id, DatosEdicionDocumento datos) => throw new NotImplementedException(); // Task 11
}
