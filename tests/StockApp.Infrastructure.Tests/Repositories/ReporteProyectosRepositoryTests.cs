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
/// Tests de integración para ReporteProyectosRepository.ObtenerAsync contra PostgreSQL real.
/// </summary>
public class ReporteProyectosRepositoryTests : PostgresRepositoryTestBase
{
    private readonly ReporteProyectosRepository _repo;

    public ReporteProyectosRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ReporteProyectosRepository(Context);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    // Npgsql exige Kind=Utc para columnas timestamptz (a diferencia de SQLite): mismo
    // criterio que ProyectoRepositoryTests.FechaLimite con DateTime.SpecifyKind. Se centraliza
    // acá para no repetir SpecifyKind en cada literal de fecha del archivo.
    private static DateTime Utc(int anio, int mes, int dia) =>
        DateTime.SpecifyKind(new DateTime(anio, mes, dia), DateTimeKind.Utc);

    // Overload con hora -- necesario para construir instantes con el offset que manda el
    // ViewModel (medianoche local de Uruguay = 03:00Z), a diferencia de Utc(y,m,d) que da
    // siempre medianoche UTC.
    private static DateTime Utc(int anio, int mes, int dia, int hora, int minuto, int segundo) =>
        DateTime.SpecifyKind(new DateTime(anio, mes, dia, hora, minuto, segundo), DateTimeKind.Utc);

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

    private static Proyecto NuevoProyecto(
        int creadoPorUsuarioId,
        EstadoProyecto estado,
        DateTime fechaCreacion,
        DateTime? fechaFin = null,
        int? zonaId = null,
        string titulo = "Proyecto de prueba") => new()
    {
        Titulo = titulo,
        CreadoPorUsuarioId = creadoPorUsuarioId,
        FechaCreacion = DateTime.SpecifyKind(fechaCreacion, DateTimeKind.Utc),
        Estado = estado,
        FechaFin = fechaFin is null ? null : DateTime.SpecifyKind(fechaFin.Value, DateTimeKind.Utc),
        ZonaId = zonaId,
    };

    private static FiltroReporteProyectos Filtro(
        AgrupadorProyectos agrupador,
        CriterioFechaProyectos criterio = CriterioFechaProyectos.Creacion,
        DateTime? desde = null,
        DateTime? hasta = null) =>
        new(agrupador, criterio, desde ?? Desde2026, hasta ?? Hasta2026);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerAsync_PorZona_CuentaProyectosPorEstado()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var centro = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(centro);
        await Context.SaveChangesAsync();

        Context.Proyectos.AddRange(
            NuevoProyecto(usuarioId, EstadoProyecto.Pendiente, Utc(2026, 3, 1), zonaId: centro.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.EnCurso, Utc(2026, 3, 2), zonaId: centro.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.Terminado, Utc(2026, 3, 3),
                fechaFin: Utc(2026, 3, 10), zonaId: centro.Id),
            // sin zona: va a "(sin asignar)"
            NuevoProyecto(usuarioId, EstadoProyecto.Cancelado, Utc(2026, 3, 4),
                fechaFin: Utc(2026, 3, 11), zonaId: null));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorProyectos.Zona));

        Assert.Equal(2, filas.Count);

        var fCentro = filas.Single(f => f.Clasificador == "Centro");
        Assert.Equal(1, fCentro.Pendientes);
        Assert.Equal(1, fCentro.EnCurso);
        Assert.Equal(1, fCentro.Terminados);
        Assert.Equal(0, fCentro.Cancelados);
        Assert.Equal(3, fCentro.Total);
        Assert.Equal(fCentro.Pendientes + fCentro.EnCurso + fCentro.Terminados + fCentro.Cancelados, fCentro.Total);

        var fSinAsignar = filas.Single(f => f.Clasificador == "(sin asignar)");
        Assert.Equal(0, fSinAsignar.Pendientes);
        Assert.Equal(0, fSinAsignar.EnCurso);
        Assert.Equal(0, fSinAsignar.Terminados);
        Assert.Equal(1, fSinAsignar.Cancelados);
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

        Context.Proyectos.AddRange(
            NuevoProyecto(usuarioId, EstadoProyecto.Pendiente, Utc(2026, 3, 1), zonaId: oeste.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.Pendiente, Utc(2026, 3, 1), zonaId: centro.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.Pendiente, Utc(2026, 3, 1), zonaId: este.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.Pendiente, Utc(2026, 3, 1), zonaId: null));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorProyectos.Zona));

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

        Context.Proyectos.AddRange(
            NuevoProyecto(usuarioId, EstadoProyecto.Pendiente, Utc(2026, 5, 1), zonaId: zona.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.EnCurso, Utc(2026, 5, 2), zonaId: zona.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.Terminado, Utc(2026, 5, 3),
                fechaFin: Utc(2026, 5, 10), zonaId: zona.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.Cancelado, Utc(2026, 5, 4),
                fechaFin: Utc(2026, 5, 11), zonaId: zona.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorProyectos.Zona, CriterioFechaProyectos.Creacion));

        var fila = Assert.Single(filas);
        Assert.Equal(1, fila.Pendientes);
        Assert.Equal(1, fila.EnCurso);
        Assert.Equal(1, fila.Terminados);
        Assert.Equal(1, fila.Cancelados);
        Assert.Equal(4, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_CriterioCierre_SoloCerrados()
    {
        // D16: con criterio Cierre, FechaFin es null en Pendiente/EnCurso -- esas dos proyectos
        // quedan FUERA del universo filtrado, así que las columnas dan cero.
        var usuarioId = await SembrarUsuarioAsync();
        var zona = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(zona);
        await Context.SaveChangesAsync();

        Context.Proyectos.AddRange(
            NuevoProyecto(usuarioId, EstadoProyecto.Pendiente, Utc(2026, 5, 1), zonaId: zona.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.EnCurso, Utc(2026, 5, 2), zonaId: zona.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.Terminado, Utc(2026, 5, 3),
                fechaFin: Utc(2026, 6, 1), zonaId: zona.Id),
            NuevoProyecto(usuarioId, EstadoProyecto.Cancelado, Utc(2026, 5, 4),
                fechaFin: Utc(2026, 6, 2), zonaId: zona.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorProyectos.Zona, CriterioFechaProyectos.Cierre));

        var fila = Assert.Single(filas);
        Assert.Equal(0, fila.Pendientes);
        Assert.Equal(0, fila.EnCurso);
        Assert.Equal(1, fila.Terminados);
        Assert.Equal(1, fila.Cancelados);
        Assert.Equal(2, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_RangoDeFechas_ExcluyeProyectosFueraDelRango()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var zona = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(zona);
        await Context.SaveChangesAsync();

        Context.Proyectos.AddRange(
            NuevoProyecto(usuarioId, EstadoProyecto.Pendiente, Utc(2026, 6, 15), zonaId: zona.Id),
            // fuera del rango 2026: no debe contarse
            NuevoProyecto(usuarioId, EstadoProyecto.Pendiente, Utc(2025, 12, 31), zonaId: zona.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorProyectos.Zona));

        var fila = Assert.Single(filas);
        Assert.Equal(1, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_HastaConOffsetUtc_IncluyeProyectoCreadaEnLasUltimas3HorasDelDiaLocal()
    {
        // El ViewModel manda Desde/Hasta como INSTANTES UTC ya convertidos desde medianoche
        // local (Uruguay UTC-3): medianoche local del 31/12 es 2026-12-31T03:00:00Z. Si el
        // repo trunca con Hasta.Date antes de sumar el día, pierde esas 3hs y el fin de rango
        // queda en 2026-12-31T23:59:59.9999999Z UTC == 2026-12-31T20:59:59 local -- una proyecto
        // creada a las 22:00 local del 31/12 (2027-01-01T01:00:00Z) queda afuera por error.
        var usuarioId = await SembrarUsuarioAsync();
        var zona = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(zona);
        await Context.SaveChangesAsync();

        var desdeConOffset = Utc(2026, 1, 1, 3, 0, 0);    // medianoche local 1/1 -> UTC
        var hastaConOffset = Utc(2026, 12, 31, 3, 0, 0);  // medianoche local 31/12 -> UTC
        var creadaUltimasHorasDelDia = Utc(2027, 1, 1, 1, 0, 0); // 22:00 local del 31/12

        Context.Proyectos.Add(
            NuevoProyecto(usuarioId, EstadoProyecto.Pendiente, creadaUltimasHorasDelDia, zonaId: zona.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(
            Filtro(AgrupadorProyectos.Zona, desde: desdeConOffset, hasta: hastaConOffset));

        var fila = Assert.Single(filas);
        Assert.Equal(1, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_AgrupaPorDimensionTematica_DevuelveNombreDelCatalogo()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var dimension = new DimensionTematica { Nombre = "Infraestructura", Activo = true };
        Context.Add(dimension);
        await Context.SaveChangesAsync();

        var proyecto = new Proyecto
        {
            Titulo = "Bacheo",
            CreadoPorUsuarioId = usuarioId,
            FechaCreacion = Utc(2026, 4, 1),
            DimensionTematicaId = dimension.Id,
        };
        Context.Proyectos.Add(proyecto);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorProyectos.DimensionTematica));

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

        var proyecto = new Proyecto
        {
            Titulo = "Poda",
            CreadoPorUsuarioId = usuarioId,
            FechaCreacion = Utc(2026, 4, 1),
            OrganismoResponsableId = organismo.Id,
        };
        Context.Proyectos.Add(proyecto);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorProyectos.OrganismoResponsable));

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

        var proyecto = new Proyecto
        {
            Titulo = "Iluminación",
            CreadoPorUsuarioId = usuarioId,
            FechaCreacion = Utc(2026, 4, 1),
            OrigenFinanciamientoId = origen.Id,
        };
        Context.Proyectos.Add(proyecto);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorProyectos.OrigenFinanciamiento));

        var fila = Assert.Single(filas);
        Assert.Equal("Fondo nacional", fila.Clasificador);
        Assert.Equal(1, fila.Total);
    }
}
