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

    public async Task IniciarProcesoAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        // Guarda de estado ORIGEN, simétrica a la de ReabrirAsync (D4/D8 del spec): en la
        // tabla del dominio, Finalizado -> EnProceso y Anulado -> EnProceso también son
        // transiciones válidas (son la reapertura). Si esta acción se limitara a llamar
        // CambiarEstado(EnProceso) sin verificar primero que el documento esté Pendiente,
        // CambiarEstado NO rechazaría iniciar el proceso de un documento cerrado — y
        // cualquier Operador con documentos.gestionar (sin documentos.administrar) podría
        // reabrir un documento cerrado sin motivo y sin pasar por ReabrirAsync. El spec solo
        // documenta la guarda simétrica del lado de ReabrirAsync; esta es la misma guarda
        // aplicada del lado de IniciarProcesoAsync.
        if (documento.Estado != EstadoDocumento.Pendiente)
            throw new ReglaDeNegocioException(
                $"No se puede iniciar el proceso de un documento en estado '{documento.Estado}': no está pendiente.");

        var estadoAnterior = documento.Estado;
        documento.CambiarEstado(EstadoDocumento.EnProceso);

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"{estadoAnterior} → {documento.Estado}", esAutomatico: true,
            anterior: estadoAnterior, nuevo: documento.Estado);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", id,
            $"{estadoAnterior} → {documento.Estado}");
    }

    public async Task VolverAPendienteAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        var estadoAnterior = documento.Estado;
        documento.CambiarEstado(EstadoDocumento.Pendiente);

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"{estadoAnterior} → {documento.Estado}", esAutomatico: true,
            anterior: estadoAnterior, nuevo: documento.Estado);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", id,
            $"{estadoAnterior} → {documento.Estado}");
    }

    public async Task FinalizarAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        var estadoAnterior = documento.Estado;
        documento.CambiarEstado(EstadoDocumento.Finalizado);
        documento.FechaCierre = DateTime.UtcNow;   // D8: lo sella el servicio, no la entidad.

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"{estadoAnterior} → {documento.Estado}", esAutomatico: true,
            anterior: estadoAnterior, nuevo: documento.Estado);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioEstadoDocumento, "DocumentoAdministrativo", id,
            $"{estadoAnterior} → {documento.Estado}");
    }

    public async Task AgregarNotaAsync(int id, string texto)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        if (string.IsNullOrWhiteSpace(texto))
            throw new ArgumentException("El texto de la nota no puede estar vacío.", nameof(texto));

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        documento.AgregarEvento(_session.UsuarioActual!.Id, texto.Trim(), esAutomatico: false);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaNotaDocumento, "DocumentoAdministrativo", id,
            $"Nota: {texto.Trim()}");
    }
    public async Task AnularAsync(int id, string motivo)
    {
        _auth.Verificar(_session, Permisos.AdministrarDocumentos);

        if (string.IsNullOrWhiteSpace(motivo))
            throw new ReglaDeNegocioException("El motivo es obligatorio para anular un documento.");

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        var estadoAnterior = documento.Estado;
        documento.CambiarEstado(EstadoDocumento.Anulado);
        documento.FechaCierre = DateTime.UtcNow;   // D8: lo sella el servicio, no la entidad.

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"Anulado: {motivo.Trim()}", esAutomatico: true,
            anterior: estadoAnterior, nuevo: documento.Estado);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AnulacionDocumento, "DocumentoAdministrativo", id,
            $"{estadoAnterior} → {documento.Estado}: {motivo.Trim()}");
    }

    public async Task ReabrirAsync(int id, string motivo)
    {
        _auth.Verificar(_session, Permisos.AdministrarDocumentos);

        if (string.IsNullOrWhiteSpace(motivo))
            throw new ReglaDeNegocioException("El motivo es obligatorio para reabrir un documento.");

        var documento = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Documento {id} no encontrado.");

        // D4/D8: la guarda es necesaria porque Pendiente -> EnProceso ya es una transición
        // válida por otra vía (IniciarProcesoAsync); sin este chequeo, "reabrir" un
        // documento que nunca estuvo cerrado no lanzaría ninguna excepción.
        if (!documento.EsCerrado)
            throw new ReglaDeNegocioException(
                $"No se puede reabrir un documento en estado '{documento.Estado}': no está cerrado.");

        var estadoAnterior = documento.Estado;
        documento.CambiarEstado(EstadoDocumento.EnProceso);
        documento.FechaCierre = null;

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"Reabierto: {motivo.Trim()}", esAutomatico: true,
            anterior: estadoAnterior, nuevo: documento.Estado);

        await _repo.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.ReaperturaDocumento, "DocumentoAdministrativo", id,
            $"{estadoAnterior} → {documento.Estado}: {motivo.Trim()}");
    }

    public Task EditarAsync(int id, DatosEdicionDocumento datos) => throw new NotImplementedException(); // Task 11
}
