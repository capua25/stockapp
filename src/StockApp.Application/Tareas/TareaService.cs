using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Tareas;

/// <summary>
/// Servicio de tareas. Patrón: auth → validación → mutación de la entidad (la máquina de
/// estados vive en Tarea.CambiarEstado) → persistencia. Tomar/Soltar/Terminar se
/// implementan en Task 4 (agrega IUsuarioRepository al constructor, para resolver nombres
/// en las notas automáticas); Cancelar/CambiarPrioridad en Task 5; AgregarNota + auditoría
/// en Task 6 (agrega IAuditLogger).
/// </summary>
public class TareaService : ITareaService
{
    private readonly ITareaRepository      _repo;
    private readonly IUsuarioRepository    _usuarios;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;

    // Clasificadores (spec 2026-09-08, D12): repositorios, NO servicios — validar
    // existencia/actividad acá no debe exigir catalogo.maestras (el permiso de ABM de
    // catálogos), que es completamente ajeno a tareas.gestionar/tareas.administrar.
    private readonly IZonaRepository                 _zonas;
    private readonly IDimensionTematicaRepository    _dimensiones;
    private readonly IOrganismoResponsableRepository _organismos;
    private readonly IOrigenFinanciamientoRepository _origenes;
    private readonly IDocumentoAdministrativoRepository _documentos;

    public TareaService(
        ITareaRepository repo, IUsuarioRepository usuarios, ICurrentSession session,
        IAuthorizationService auth, IAuditLogger audit,
        IZonaRepository zonas, IDimensionTematicaRepository dimensiones,
        IOrganismoResponsableRepository organismos, IOrigenFinanciamientoRepository origenes,
        IDocumentoAdministrativoRepository documentos)
    {
        _repo        = repo;
        _usuarios    = usuarios;
        _session     = session;
        _auth        = auth;
        _audit       = audit;
        _zonas       = zonas;
        _dimensiones = dimensiones;
        _organismos  = organismos;
        _origenes    = origenes;
        _documentos  = documentos;
    }

    public async Task<int> CrearAsync(Tarea tarea)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);

        if (string.IsNullOrWhiteSpace(tarea.Titulo))
            throw new ArgumentException("El título de la tarea es obligatorio.", nameof(tarea.Titulo));

        // D12 del spec: misma validación de clasificadores que ReclasificarAsync (Task 5) —
        // no hay atajo por la vía del alta.
        await ValidarClasificacionAsync(
            tarea.ZonaId, tarea.DimensionTematicaId, tarea.OrganismoResponsableId,
            tarea.OrigenFinanciamientoId, tarea.DocumentoAdministrativoId);

        // Decisión 8 del spec: la prioridad nace SIEMPRE en Media, incluso si el llamador
        // (Admin incluido) trae otra cosa en la entidad.
        tarea.Estado              = EstadoTarea.Pendiente;
        tarea.Prioridad           = PrioridadTarea.Media;
        tarea.CreadaPorUsuarioId  = _session.UsuarioActual!.Id;
        tarea.FechaCreacion       = DateTime.UtcNow;
        tarea.TomadaPorUsuarioId  = null;
        tarea.FechaInicio         = null;
        tarea.CerradaPorUsuarioId = null;
        tarea.FechaFin            = null;

        var id = await _repo.AgregarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaTarea, "Tarea", id,
            $"Título: {tarea.Titulo}" +
            (tarea.FechaLimite is not null ? $"; Vence: {tarea.FechaLimite:yyyy-MM-dd}" : string.Empty));

        return id;
    }

    /// <summary>
    /// Validación compartida de los cinco clasificadores (spec 2026-09-08, D12): cada
    /// catálogo asignado debe existir y estar activo, y el documento debe ser de tipo
    /// Expediente y estar activo (EsActivo). La reutilizan CrearAsync y ReclasificarAsync
    /// (Task 5) SIN atajos — un Admin no puede colar un Oficio ni un catálogo inactivo por
    /// la vía de la reclasificación. Los cinco ids nulos (caso mayoritario, D7) no disparan
    /// ninguna consulta.
    /// </summary>
    private async Task<(Zona? Zona, DimensionTematica? Dimension, OrganismoResponsable? Organismo,
                         OrigenFinanciamiento? Origen, DocumentoAdministrativo? Documento)>
        ValidarClasificacionAsync(
            int? zonaId, int? dimensionId, int? organismoId, int? origenId, int? documentoId)
    {
        Zona? zona = null;
        if (zonaId is int zid)
        {
            zona = await _zonas.ObtenerPorIdAsync(zid)
                ?? throw new ReglaDeNegocioException($"La zona {zid} no existe.");
            if (!zona.Activo)
                throw new ReglaDeNegocioException($"La zona '{zona.Nombre}' está inactiva.");
        }

        DimensionTematica? dimension = null;
        if (dimensionId is int did)
        {
            dimension = await _dimensiones.ObtenerPorIdAsync(did)
                ?? throw new ReglaDeNegocioException($"La dimensión temática {did} no existe.");
            if (!dimension.Activo)
                throw new ReglaDeNegocioException($"La dimensión temática '{dimension.Nombre}' está inactiva.");
        }

        OrganismoResponsable? organismo = null;
        if (organismoId is int oid)
        {
            organismo = await _organismos.ObtenerPorIdAsync(oid)
                ?? throw new ReglaDeNegocioException($"El organismo responsable {oid} no existe.");
            if (!organismo.Activo)
                throw new ReglaDeNegocioException($"El organismo responsable '{organismo.Nombre}' está inactivo.");
        }

        OrigenFinanciamiento? origen = null;
        if (origenId is int fid)
        {
            origen = await _origenes.ObtenerPorIdAsync(fid)
                ?? throw new ReglaDeNegocioException($"El origen de financiamiento {fid} no existe.");
            if (!origen.Activo)
                throw new ReglaDeNegocioException($"El origen de financiamiento '{origen.Nombre}' está inactivo.");
        }

        DocumentoAdministrativo? documento = null;
        if (documentoId is int docid)
        {
            documento = await _documentos.ObtenerPorIdAsync(docid)
                ?? throw new ReglaDeNegocioException($"El documento {docid} no existe.");
            if (documento.Tipo != TipoDocumento.Expediente)
                throw new ReglaDeNegocioException(
                    $"El documento {docid} no es un expediente (es {documento.Tipo}).");
            if (!documento.EsActivo)
                throw new ReglaDeNegocioException(
                    $"El expediente {documento.Numero}/{documento.Anio} no está activo.");
        }

        return (zona, dimension, organismo, origen, documento);
    }

    public async Task<IReadOnlyList<Tarea>> ListarAsync()
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);
        return await _repo.ListarAsync();
    }

    public async Task TomarAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var estadoAnterior = tarea.Estado;
        tarea.CambiarEstado(EstadoTarea.EnCurso);
        tarea.TomadaPorUsuarioId = _session.UsuarioActual!.Id;
        tarea.FechaInicio        = DateTime.UtcNow;

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioEstadoTarea, "Tarea", id,
            $"{estadoAnterior} → {tarea.Estado} (tomada)");
    }

    public async Task SoltarAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var actorId = _session.UsuarioActual!.Id;
        var tomadorAnteriorId = tarea.TomadaPorUsuarioId;
        var estadoAnterior = tarea.Estado;

        tarea.CambiarEstado(EstadoTarea.Pendiente);
        tarea.TomadaPorUsuarioId = null;
        tarea.FechaInicio        = null;

        // Decisión 11 del spec: toda acción sobre una tarea ajena genera nota automática.
        if (tomadorAnteriorId is int tomadorId && tomadorId != actorId)
            tarea.Notas.Add(await NotaAjenaAsync(actorId, tomadorId, "soltó"));

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            actorId, AccionAuditada.CambioEstadoTarea, "Tarea", id,
            $"{estadoAnterior} → {tarea.Estado} (soltada)");
    }

    public async Task TerminarAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var actorId = _session.UsuarioActual!.Id;
        var tomadorId = tarea.TomadaPorUsuarioId;
        var estadoAnterior = tarea.Estado;

        tarea.CambiarEstado(EstadoTarea.Terminada);
        tarea.CerradaPorUsuarioId = actorId;
        tarea.FechaFin            = DateTime.UtcNow;

        if (tomadorId is int idTomador && idTomador != actorId)
            tarea.Notas.Add(await NotaAjenaAsync(actorId, idTomador, "terminó"));

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            actorId, AccionAuditada.CambioEstadoTarea, "Tarea", id,
            $"{estadoAnterior} → {tarea.Estado}");
    }

    /// <summary>
    /// Formato exacto de la decisión 11 del spec: "García terminó una tarea tomada por Juan".
    /// Fix (review final, Critical): el actor se resuelve contra la base igual que el
    /// tomador, NUNCA desde _session.UsuarioActual.NombreUsuario — HttpCurrentSession (el
    /// ICurrentSession real de la API) siempre lo arma vacío (ver comentario en
    /// HttpCurrentSession), así que la nota salía sin el nombre de quien actuó.
    /// </summary>
    private async Task<NotaTarea> NotaAjenaAsync(int actorId, int tomadorId, string verbo)
    {
        var actor         = await _usuarios.ObtenerPorIdAsync(actorId);
        var actorNombre   = actor?.NombreUsuario ?? $"usuario {actorId}";
        var tomador       = await _usuarios.ObtenerPorIdAsync(tomadorId);
        var tomadorNombre = tomador?.NombreUsuario ?? $"usuario {tomadorId}";

        return new NotaTarea
        {
            UsuarioId    = actorId,
            Fecha        = DateTime.UtcNow,
            Texto        = $"{actorNombre} {verbo} una tarea tomada por {tomadorNombre}.",
            EsAutomatica = true,
        };
    }

    public async Task CancelarAsync(int id)
    {
        _auth.Verificar(_session, Permisos.AdministrarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var actorId = _session.UsuarioActual!.Id;
        var tomadorId = tarea.TomadaPorUsuarioId;

        tarea.CambiarEstado(EstadoTarea.Cancelada);
        tarea.CerradaPorUsuarioId = actorId;
        tarea.FechaFin            = DateTime.UtcNow;

        // Fix (review final, Important): cancelar una tarea ajena es la acción que MÁS
        // necesita quedar registrada (decisiones 9 y 11 del spec) — antes solo Soltar y
        // Terminar generaban nota automática.
        if (tomadorId is int idTomador && idTomador != actorId)
            tarea.Notas.Add(await NotaAjenaAsync(actorId, idTomador, "canceló"));

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            actorId, AccionAuditada.CancelacionTarea, "Tarea", id,
            $"Cancelación de '{tarea.Titulo}'");
    }

    public async Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad)
    {
        _auth.Verificar(_session, Permisos.AdministrarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        if (tarea.Prioridad == prioridad)
            return;   // sin cambios: no hay nada que registrar

        var anterior = tarea.Prioridad;
        tarea.CambiarPrioridad(prioridad);
        // Decisión 9 del spec: cada cambio de prioridad genera nota automática.
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId    = _session.UsuarioActual!.Id,
            Fecha        = DateTime.UtcNow,
            Texto        = $"Prioridad: {anterior} → {prioridad}",
            EsAutomatica = true,
        });

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioPrioridadTarea, "Tarea", id,
            $"{anterior} → {prioridad}");
    }

    public async Task AgregarNotaAsync(int id, string texto)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);

        if (string.IsNullOrWhiteSpace(texto))
            throw new ArgumentException("El texto de la nota no puede estar vacío.", nameof(texto));

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var nota = new NotaTarea
        {
            UsuarioId    = _session.UsuarioActual!.Id,
            Fecha        = DateTime.UtcNow,
            Texto        = texto.Trim(),
            EsAutomatica = false,
        };
        tarea.Notas.Add(nota);
        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaNotaTarea, "Tarea", id,
            $"Nota: {nota.Texto}");
    }

    public async Task<Tarea?> ObtenerPorIdAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);
        return await _repo.ObtenerPorIdAsync(id);
    }

    public async Task ReclasificarAsync(int tareaId, DatosClasificacionTarea datos)
    {
        _auth.Verificar(_session, Permisos.AdministrarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(tareaId)
            ?? throw new EntidadNoEncontradaException($"Tarea {tareaId} no encontrada.");

        // D12: misma validación que CrearAsync, sin atajos.
        var (zona, dimension, organismo, origen, documento) = await ValidarClasificacionAsync(
            datos.ZonaId, datos.DimensionTematicaId, datos.OrganismoResponsableId,
            datos.OrigenFinanciamientoId, datos.DocumentoAdministrativoId);

        var huboCambios =
            tarea.ZonaId != datos.ZonaId ||
            tarea.DimensionTematicaId != datos.DimensionTematicaId ||
            tarea.OrganismoResponsableId != datos.OrganismoResponsableId ||
            tarea.OrigenFinanciamientoId != datos.OrigenFinanciamientoId ||
            tarea.DocumentoAdministrativoId != datos.DocumentoAdministrativoId;

        // Guard "sin cambios" (D10 del spec, mismo patrón que CambiarPrioridadAsync,
        // TareaService.cs): si la reclasificación no cambia ningún valor, no se genera
        // nota automática ni entrada de auditoría.
        if (!huboCambios)
            return;

        var diff = ConstruirDiffClasificacion(tarea, zona, dimension, organismo, origen, documento);

        // D9: reclasificar NO toca Estado — alcanza también tareas Terminada/Cancelada.
        tarea.ZonaId = datos.ZonaId;
        tarea.DimensionTematicaId = datos.DimensionTematicaId;
        tarea.OrganismoResponsableId = datos.OrganismoResponsableId;
        tarea.OrigenFinanciamientoId = datos.OrigenFinanciamientoId;
        tarea.DocumentoAdministrativoId = datos.DocumentoAdministrativoId;

        // D10: doble registro — nota automática en el hilo + entrada de auditoría, mismo
        // patrón que AnularAsync en Documentos.
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId    = _session.UsuarioActual!.Id,
            Fecha        = DateTime.UtcNow,
            Texto        = $"admin reclasificó — {diff}",
            EsAutomatica = true,
        });

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.ReclasificacionTarea, "Tarea", tareaId, diff);
    }

    /// <summary>
    /// Diff legible de la reclasificación (D10 del spec), ej.: "Zona: (sin asignar) →
    /// Centro; Dimensión: Tránsito → Infraestructura". Solo incluye los campos que
    /// efectivamente cambiaron — <see cref="ReclasificarAsync"/> ya filtró el caso "sin
    /// cambios" antes de llamar acá, pero un cambio parcial (ej. solo Zona) no debe listar
    /// los otros cuatro campos sin cambiar.
    /// </summary>
    private static string ConstruirDiffClasificacion(
        Tarea tarea, Zona? nuevaZona, DimensionTematica? nuevaDimension,
        OrganismoResponsable? nuevoOrganismo, OrigenFinanciamiento? nuevoOrigen,
        DocumentoAdministrativo? nuevoDocumento)
    {
        var partes = new List<string>();

        void AgregarSiCambio(string etiqueta, string? anterior, string? nuevo)
        {
            var anteriorTexto = anterior ?? "(sin asignar)";
            var nuevoTexto = nuevo ?? "(sin asignar)";
            if (anteriorTexto != nuevoTexto)
                partes.Add($"{etiqueta}: {anteriorTexto} → {nuevoTexto}");
        }

        AgregarSiCambio("Zona", tarea.Zona?.Nombre, nuevaZona?.Nombre);
        AgregarSiCambio("Dimensión", tarea.DimensionTematica?.Nombre, nuevaDimension?.Nombre);
        AgregarSiCambio("Organismo", tarea.OrganismoResponsable?.Nombre, nuevoOrganismo?.Nombre);
        AgregarSiCambio("Origen de financiamiento", tarea.OrigenFinanciamiento?.Nombre, nuevoOrigen?.Nombre);
        AgregarSiCambio(
            "Expediente",
            tarea.DocumentoAdministrativo is null ? null : $"{tarea.DocumentoAdministrativo.Numero}/{tarea.DocumentoAdministrativo.Anio}",
            nuevoDocumento is null ? null : $"{nuevoDocumento.Numero}/{nuevoDocumento.Anio}");

        return string.Join("; ", partes);
    }

    public async Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);
        return await _repo.ListarPorDocumentoAsync(documentoId);
    }
}
