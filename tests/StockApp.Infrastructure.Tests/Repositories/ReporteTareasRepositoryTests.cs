using Microsoft.EntityFrameworkCore;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests de integración para ReporteTareasRepository.ObtenerAsync contra PostgreSQL real.
/// </summary>
public class ReporteTareasRepositoryTests : PostgresRepositoryTestBase
{
    private readonly ReporteTareasRepository _repo;

    public ReporteTareasRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ReporteTareasRepository(Context);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    // Npgsql exige Kind=Utc para columnas timestamptz (a diferencia de SQLite): mismo
    // criterio que TareaRepositoryTests.FechaLimite con DateTime.SpecifyKind. Se centraliza
    // acá para no repetir SpecifyKind en cada literal de fecha del archivo.
    private static DateTime Utc(int anio, int mes, int dia) =>
        DateTime.SpecifyKind(new DateTime(anio, mes, dia), DateTimeKind.Utc);

    private static readonly DateTime Desde2026 = Utc(2026, 1, 1);
    private static readonly DateTime Hasta2026 = Utc(2026, 12, 31);

    private async Task<int> SembrarUsuarioAsync()
    {
        var usuario = new Usuario
        {
            NombreUsuario = "operario1",
            HashContrasena = "x",
            Rol = RolUsuario.Operador,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();
        return usuario.Id;
    }

    private static Tarea NuevaTarea(
        int creadaPorUsuarioId,
        EstadoTarea estado,
        DateTime fechaCreacion,
        DateTime? fechaFin = null,
        int? zonaId = null,
        int? documentoAdministrativoId = null,
        string titulo = "Tarea de prueba") => new()
    {
        Titulo = titulo,
        CreadaPorUsuarioId = creadaPorUsuarioId,
        FechaCreacion = DateTime.SpecifyKind(fechaCreacion, DateTimeKind.Utc),
        Estado = estado,
        FechaFin = fechaFin is null ? null : DateTime.SpecifyKind(fechaFin.Value, DateTimeKind.Utc),
        ZonaId = zonaId,
        DocumentoAdministrativoId = documentoAdministrativoId,
    };

    private static FiltroReporteTareas Filtro(
        AgrupadorTareas agrupador,
        CriterioFechaTareas criterio = CriterioFechaTareas.Creacion,
        DateTime? desde = null,
        DateTime? hasta = null) =>
        new(agrupador, criterio, desde ?? Desde2026, hasta ?? Hasta2026);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerAsync_AgrupaPorZona_ConSinAsignar_LasFilasSumanElTotal()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var centro = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(centro);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2026, 3, 1), zonaId: centro.Id),
            NuevaTarea(usuarioId, EstadoTarea.EnCurso, Utc(2026, 3, 2), zonaId: centro.Id),
            NuevaTarea(usuarioId, EstadoTarea.Terminada, Utc(2026, 3, 3),
                fechaFin: Utc(2026, 3, 10), zonaId: centro.Id),
            // sin zona: va a "(sin asignar)"
            NuevaTarea(usuarioId, EstadoTarea.Cancelada, Utc(2026, 3, 4),
                fechaFin: Utc(2026, 3, 11), zonaId: null));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Zona));

        Assert.Equal(2, filas.Count);

        var fCentro = filas.Single(f => f.Clasificador == "Centro");
        Assert.Equal(1, fCentro.Pendientes);
        Assert.Equal(1, fCentro.EnCurso);
        Assert.Equal(1, fCentro.Terminadas);
        Assert.Equal(0, fCentro.Canceladas);
        Assert.Equal(3, fCentro.Total);
        Assert.Equal(fCentro.Pendientes + fCentro.EnCurso + fCentro.Terminadas + fCentro.Canceladas, fCentro.Total);

        var fSinAsignar = filas.Single(f => f.Clasificador == "(sin asignar)");
        Assert.Equal(0, fSinAsignar.Pendientes);
        Assert.Equal(0, fSinAsignar.EnCurso);
        Assert.Equal(0, fSinAsignar.Terminadas);
        Assert.Equal(1, fSinAsignar.Canceladas);
        Assert.Equal(1, fSinAsignar.Total);
    }

    [Fact]
    public async Task ObtenerAsync_OrdenAlfabetico_ConSinAsignarSiempreAlFinal()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var oeste = new Zona { Nombre = "Oeste", Activo = true };
        var centro = new Zona { Nombre = "Centro", Activo = true };
        var este = new Zona { Nombre = "Este", Activo = true };
        Context.AddRange(oeste, centro, este);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2026, 3, 1), zonaId: oeste.Id),
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2026, 3, 1), zonaId: centro.Id),
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2026, 3, 1), zonaId: este.Id),
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2026, 3, 1), zonaId: null));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Zona));

        Assert.Equal(
            new[] { "Centro", "Este", "Oeste", "(sin asignar)" },
            filas.Select(f => f.Clasificador).ToArray());
    }

    [Fact]
    public async Task ObtenerAsync_CriterioCreacion_CuentaLosCuatroEstados()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var zona = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(zona);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2026, 5, 1), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.EnCurso, Utc(2026, 5, 2), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.Terminada, Utc(2026, 5, 3),
                fechaFin: Utc(2026, 5, 10), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.Cancelada, Utc(2026, 5, 4),
                fechaFin: Utc(2026, 5, 11), zonaId: zona.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Zona, CriterioFechaTareas.Creacion));

        var fila = Assert.Single(filas);
        Assert.Equal(1, fila.Pendientes);
        Assert.Equal(1, fila.EnCurso);
        Assert.Equal(1, fila.Terminadas);
        Assert.Equal(1, fila.Canceladas);
        Assert.Equal(4, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_CriterioCierre_SoloCuentaCerradas_PendientesYEnCursoEnCero()
    {
        // D16: con criterio Cierre, FechaFin es null en Pendiente/EnCurso -- esas dos tareas
        // quedan FUERA del universo filtrado, así que las columnas dan cero.
        var usuarioId = await SembrarUsuarioAsync();
        var zona = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(zona);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2026, 5, 1), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.EnCurso, Utc(2026, 5, 2), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.Terminada, Utc(2026, 5, 3),
                fechaFin: Utc(2026, 6, 1), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.Cancelada, Utc(2026, 5, 4),
                fechaFin: Utc(2026, 6, 2), zonaId: zona.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Zona, CriterioFechaTareas.Cierre));

        var fila = Assert.Single(filas);
        Assert.Equal(0, fila.Pendientes);
        Assert.Equal(0, fila.EnCurso);
        Assert.Equal(1, fila.Terminadas);
        Assert.Equal(1, fila.Canceladas);
        Assert.Equal(2, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_RangoDeFechas_ExcluyeTareasFueraDelRango()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var zona = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(zona);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2026, 6, 15), zonaId: zona.Id),
            // fuera del rango 2026: no debe contarse
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2025, 12, 31), zonaId: zona.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Zona));

        var fila = Assert.Single(filas);
        Assert.Equal(1, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_AgrupaPorExpediente_EtiquetaNumeroBarraAnio()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var documento = new DocumentoAdministrativo
        {
            Numero = "0087",
            Anio = 2026,
            Tipo = TipoDocumento.Expediente,
            FechaEmision = Utc(2026, 1, 15),
            Descripcion = "Expediente de prueba",
            RegistradoPorUsuarioId = usuarioId,
            FechaRegistro = DateTime.UtcNow,
        };
        Context.Add(documento);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2026, 4, 1),
                documentoAdministrativoId: documento.Id),
            // sin expediente: "(sin asignar)"
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, Utc(2026, 4, 2)));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Expediente));

        Assert.Equal(2, filas.Count);
        Assert.Contains(filas, f => f.Clasificador == "0087/2026" && f.Total == 1);
        Assert.Contains(filas, f => f.Clasificador == "(sin asignar)" && f.Total == 1);
    }

    [Fact]
    public async Task ObtenerAsync_AgrupaPorDimensionTematica_DevuelveNombreDelCatalogo()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var dimension = new DimensionTematica { Nombre = "Infraestructura", Activo = true };
        Context.Add(dimension);
        await Context.SaveChangesAsync();

        var tarea = new Tarea
        {
            Titulo = "Bacheo",
            CreadaPorUsuarioId = usuarioId,
            FechaCreacion = Utc(2026, 4, 1),
            DimensionTematicaId = dimension.Id,
        };
        Context.Tareas.Add(tarea);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.DimensionTematica));

        var fila = Assert.Single(filas);
        Assert.Equal("Infraestructura", fila.Clasificador);
        Assert.Equal(1, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_AgrupaPorOrganismoResponsable_DevuelveNombreDelCatalogo()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var organismo = new OrganismoResponsable { Nombre = "Intendencia", Activo = true };
        Context.Add(organismo);
        await Context.SaveChangesAsync();

        var tarea = new Tarea
        {
            Titulo = "Poda",
            CreadaPorUsuarioId = usuarioId,
            FechaCreacion = Utc(2026, 4, 1),
            OrganismoResponsableId = organismo.Id,
        };
        Context.Tareas.Add(tarea);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.OrganismoResponsable));

        var fila = Assert.Single(filas);
        Assert.Equal("Intendencia", fila.Clasificador);
        Assert.Equal(1, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_AgrupaPorOrigenFinanciamiento_DevuelveNombreDelCatalogo()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var origen = new OrigenFinanciamiento { Nombre = "Fondo nacional", Activo = true };
        Context.Add(origen);
        await Context.SaveChangesAsync();

        var tarea = new Tarea
        {
            Titulo = "Iluminación",
            CreadaPorUsuarioId = usuarioId,
            FechaCreacion = Utc(2026, 4, 1),
            OrigenFinanciamientoId = origen.Id,
        };
        Context.Tareas.Add(tarea);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.OrigenFinanciamiento));

        var fila = Assert.Single(filas);
        Assert.Equal("Fondo nacional", fila.Clasificador);
        Assert.Equal(1, fila.Total);
    }
}
