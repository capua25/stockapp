using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Proyectos;

/// <summary>
/// Servicio de proyectos (spec 2026-10-30). Patrón: auth → validación → mutación (la máquina
/// de estados vive en Proyecto.CambiarEstado) → persistencia → auditoría. Los catálogos se
/// validan contra sus repositorios, no contra servicios: no exige catalogo.maestras.
/// </summary>
public class ProyectoService : IProyectoService
{
    private readonly IProyectoRepository               _repo;
    private readonly ICurrentSession                   _session;
    private readonly IAuthorizationService             _auth;
    private readonly IAuditLogger                      _audit;
    private readonly IZonaRepository                   _zonas;
    private readonly IDimensionTematicaRepository      _dimensiones;
    private readonly IOrganismoResponsableRepository   _organismos;
    private readonly IOrigenFinanciamientoRepository   _origenes;

    public ProyectoService(
        IProyectoRepository repo, ICurrentSession session, IAuthorizationService auth, IAuditLogger audit,
        IZonaRepository zonas, IDimensionTematicaRepository dimensiones,
        IOrganismoResponsableRepository organismos, IOrigenFinanciamientoRepository origenes)
    {
        _repo        = repo;
        _session     = session;
        _auth        = auth;
        _audit       = audit;
        _zonas       = zonas;
        _dimensiones = dimensiones;
        _organismos  = organismos;
        _origenes    = origenes;
    }

    public async Task<int> CrearAsync(DatosProyecto datos)
    {
        _auth.Verificar(_session, Permisos.GestionarProyectos);
        ValidarCampos(datos);
        await ValidarClasificacionAsync(datos);

        var proyecto = new Proyecto
        {
            Titulo                 = datos.Titulo.Trim(),
            Descripcion            = datos.Descripcion,
            Estado                 = EstadoProyecto.Pendiente,
            Prioridad              = datos.Prioridad,
            FechaLimite            = datos.FechaLimite,
            Presupuesto            = datos.Presupuesto,
            CreadoPorUsuarioId     = _session.UsuarioActual!.Id,
            FechaCreacion          = DateTime.UtcNow,
            FechaFin               = null,
            ZonaId                 = datos.ZonaId,
            DimensionTematicaId    = datos.DimensionTematicaId,
            OrganismoResponsableId = datos.OrganismoResponsableId,
            OrigenFinanciamientoId = datos.OrigenFinanciamientoId,
        };

        var id = await _repo.AgregarAsync(proyecto);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaProyecto, "Proyecto", id,
            $"Título: {proyecto.Titulo}" +
            (proyecto.Presupuesto is not null ? $"; Presupuesto: {proyecto.Presupuesto}" : string.Empty));

        return id;
    }

    public async Task ModificarAsync(int id, DatosProyecto datos)
    {
        _auth.Verificar(_session, Permisos.GestionarProyectos);

        var proyecto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Proyecto {id} no encontrado.");

        if (proyecto.EsCerrado)
            throw new ReglaDeNegocioException("Reabrí el proyecto para editarlo.");

        ValidarCampos(datos);
        var (zona, dimension, organismo, origen) = await ValidarClasificacionAsync(datos);

        var titulo = datos.Titulo.Trim();
        var cambios = new List<string>();
        void Comparar<T>(string campo, T viejo, T nuevo)
        {
            if (!EqualityComparer<T>.Default.Equals(viejo, nuevo))
                cambios.Add($"{campo}: {viejo?.ToString() ?? "(sin asignar)"} → {nuevo?.ToString() ?? "(sin asignar)"}");
        }

        Comparar("Título", proyecto.Titulo, titulo);
        Comparar("Descripción", proyecto.Descripcion, datos.Descripcion);
        Comparar("Prioridad", proyecto.Prioridad, datos.Prioridad);
        Comparar("Fecha límite", proyecto.FechaLimite, datos.FechaLimite);
        Comparar("Presupuesto", proyecto.Presupuesto, datos.Presupuesto);
        Comparar("Zona", proyecto.Zona?.Nombre ?? ConId(proyecto.ZonaId), zona?.Nombre);
        Comparar("Dimensión", proyecto.DimensionTematica?.Nombre ?? ConId(proyecto.DimensionTematicaId), dimension?.Nombre);
        Comparar("Organismo", proyecto.OrganismoResponsable?.Nombre ?? ConId(proyecto.OrganismoResponsableId), organismo?.Nombre);
        Comparar("Origen de financiamiento", proyecto.OrigenFinanciamiento?.Nombre ?? ConId(proyecto.OrigenFinanciamientoId), origen?.Nombre);

        if (cambios.Count == 0)
            return;   // sin cambios: no hay nada que persistir ni auditar

        proyecto.Titulo                 = titulo;
        proyecto.Descripcion            = datos.Descripcion;
        proyecto.Prioridad              = datos.Prioridad;
        proyecto.FechaLimite            = datos.FechaLimite;
        proyecto.Presupuesto            = datos.Presupuesto;
        // Solo las FKs, nunca las navs (mismo criterio que GastoService.ModificarAsync).
        proyecto.ZonaId                 = datos.ZonaId;
        proyecto.DimensionTematicaId    = datos.DimensionTematicaId;
        proyecto.OrganismoResponsableId = datos.OrganismoResponsableId;
        proyecto.OrigenFinanciamientoId = datos.OrigenFinanciamientoId;

        await _repo.ActualizarAsync(proyecto);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.ModificacionProyecto, "Proyecto", id,
            string.Join("; ", cambios));
    }

    // Texto del valor anterior cuando la nav no vino cargada pero la FK sí.
    private static string? ConId(int? id) => id?.ToString();

    public async Task CambiarEstadoAsync(int id, EstadoProyecto destino)
    {
        _auth.Verificar(_session, Permisos.GestionarProyectos);

        var proyecto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Proyecto {id} no encontrado.");

        var anterior = proyecto.Estado;
        proyecto.CambiarEstado(destino, DateTime.UtcNow);

        await _repo.ActualizarAsync(proyecto);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioEstadoProyecto, "Proyecto", id,
            $"{anterior} → {proyecto.Estado}");
    }

    public async Task AgregarNotaAsync(int id, string texto)
    {
        _auth.Verificar(_session, Permisos.GestionarProyectos);

        if (string.IsNullOrWhiteSpace(texto))
            throw new ArgumentException("El texto de la nota no puede estar vacío.", nameof(texto));

        var proyecto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Proyecto {id} no encontrado.");

        var nota = new NotaProyecto
        {
            UsuarioId    = _session.UsuarioActual!.Id,
            Fecha        = DateTime.UtcNow,
            Texto        = texto.Trim(),
            EsAutomatica = false,
        };
        proyecto.Notas.Add(nota);
        await _repo.ActualizarAsync(proyecto);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaNotaProyecto, "Proyecto", id,
            $"Nota: {nota.Texto}");
    }

    public async Task<IReadOnlyList<Proyecto>> ListarAsync()
    {
        _auth.Verificar(_session, Permisos.VerProyectos);
        return await _repo.ListarAsync();
    }

    public async Task<FichaProyectoDto> ObtenerFichaAsync(int id)
    {
        _auth.Verificar(_session, Permisos.VerProyectos);

        var proyecto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Proyecto {id} no encontrado.");

        var gastos = await _repo.ListarGastosActivosAsync(id);
        var total = gastos.Sum(g => g.MontoTotal);
        // Presupuesto 0 es distinto de "sin presupuesto": da disponible negativo, no null.
        decimal? disponible = proyecto.Presupuesto is decimal p ? p - total : null;

        IReadOnlyList<GastoDeProyectoDto>? detalle = null;
        if (_session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerFinanzas))
            detalle = gastos
                .Select(g => new GastoDeProyectoDto(
                    g.Id, g.Fecha, g.Proveedor?.Nombre ?? string.Empty, g.Detalle, g.MontoTotal))
                .ToList();

        return new FichaProyectoDto(proyecto, total, disponible, detalle);
    }

    public async Task<IReadOnlyList<OpcionProyectoDto>> ListarOpcionesAsync()
    {
        if (!_session.EstaAutenticado)
            throw new UnauthorizedAccessException("No hay sesión activa.");

        var proyectos = await _repo.ListarAsync();
        return proyectos
            .Where(p => p.Estado is EstadoProyecto.Pendiente or EstadoProyecto.EnCurso)
            .OrderBy(p => p.Titulo, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new OpcionProyectoDto(p.Id, p.Titulo, p.Estado))
            .ToList();
    }

    private static void ValidarCampos(DatosProyecto datos)
    {
        if (string.IsNullOrWhiteSpace(datos.Titulo))
            throw new ArgumentException("El título del proyecto es obligatorio.", nameof(datos));
        if (datos.Presupuesto is < 0)
            throw new ReglaDeNegocioException("El presupuesto no puede ser negativo.");
    }

    /// <summary>
    /// Misma regla que TareaService.ValidarClasificacionAsync: cada catálogo asignado debe
    /// existir y estar activo. Ids nulos no disparan consultas.
    /// </summary>
    private async Task<(Zona? Zona, DimensionTematica? Dimension, OrganismoResponsable? Organismo,
                         OrigenFinanciamiento? Origen)>
        ValidarClasificacionAsync(DatosProyecto datos)
    {
        Zona? zona = null;
        if (datos.ZonaId is int zid)
        {
            zona = await _zonas.ObtenerPorIdAsync(zid)
                ?? throw new ReglaDeNegocioException($"La zona {zid} no existe.");
            if (!zona.Activo)
                throw new ReglaDeNegocioException($"La zona '{zona.Nombre}' está inactiva.");
        }

        DimensionTematica? dimension = null;
        if (datos.DimensionTematicaId is int did)
        {
            dimension = await _dimensiones.ObtenerPorIdAsync(did)
                ?? throw new ReglaDeNegocioException($"La dimensión temática {did} no existe.");
            if (!dimension.Activo)
                throw new ReglaDeNegocioException($"La dimensión temática '{dimension.Nombre}' está inactiva.");
        }

        OrganismoResponsable? organismo = null;
        if (datos.OrganismoResponsableId is int oid)
        {
            organismo = await _organismos.ObtenerPorIdAsync(oid)
                ?? throw new ReglaDeNegocioException($"El organismo responsable {oid} no existe.");
            if (!organismo.Activo)
                throw new ReglaDeNegocioException($"El organismo responsable '{organismo.Nombre}' está inactivo.");
        }

        OrigenFinanciamiento? origen = null;
        if (datos.OrigenFinanciamientoId is int fid)
        {
            origen = await _origenes.ObtenerPorIdAsync(fid)
                ?? throw new ReglaDeNegocioException($"El origen de financiamiento {fid} no existe.");
            if (!origen.Activo)
                throw new ReglaDeNegocioException($"El origen de financiamiento '{origen.Nombre}' está inactivo.");
        }

        return (zona, dimension, organismo, origen);
    }
}
