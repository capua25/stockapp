using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class TareaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly TareaRepository _repo;

    public TareaRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new TareaRepository(Context);
    }

    private static Tarea NuevaTarea(string titulo = "Reparar bache") => new()
    {
        Titulo = titulo,
        CreadaPorUsuarioId = 1,
        FechaCreacion = DateTime.UtcNow,
    };

    private static Usuario NuevoUsuario(string nombreUsuario = "operario1") => new()
    {
        NombreUsuario = nombreUsuario,
        HashContrasena = "x",
        Rol = RolUsuario.Operador,
        FechaAlta = DateTime.UtcNow,
    };

    [Fact]
    public async Task AgregarAsync_ConNotas_Y_ObtenerPorId_TraeElHiloCompleto()
    {
        var tarea = NuevaTarea();
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId = 1, Fecha = DateTime.UtcNow, Texto = "primera nota", EsAutomatica = false,
        });

        var id = await _repo.AgregarAsync(tarea);
        Context.ChangeTracker.Clear();

        var encontrada = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(encontrada);
        Assert.Equal("Reparar bache", encontrada!.Titulo);
        Assert.Equal(EstadoTarea.Pendiente, encontrada.Estado);
        Assert.Equal(PrioridadTarea.Media, encontrada.Prioridad);
        var nota = Assert.Single(encontrada.Notas);
        Assert.Equal("primera nota", nota.Texto);
    }

    [Fact]
    public async Task ListarAsync_DevuelveTodasLasTareas_SinFiltrarPorUsuario()
    {
        await _repo.AgregarAsync(NuevaTarea("Tarea A"));
        await _repo.AgregarAsync(NuevaTarea("Tarea B"));
        Context.ChangeTracker.Clear();

        var todas = await _repo.ListarAsync();

        Assert.Equal(2, todas.Count);
    }

    [Fact]
    public async Task ObtenerPorId_NotasOrdenadasPorFecha()
    {
        var tarea = NuevaTarea();
        var id = await _repo.AgregarAsync(tarea);

        // Desvío deliberado del brief (task-2-brief.md): el snippet original insertaba
        // "vieja" (Fecha -10min) ANTES que "nueva" (Fecha ahora), es decir en el mismo
        // orden en que Id/inserción las ordena. Con ese orden el test pasa aunque el
        // repositorio no ordene por Fecha (alcanza con el orden natural de inserción/Id).
        // Acá se insertan al revés — "nueva" primero, "vieja" después — para que el
        // OrderBy(n => n.Fecha) de TareaRepository.ConIncludes sea lo que realmente se
        // verifica, no un efecto colateral del orden de Id.
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId = 1, Fecha = DateTime.UtcNow, Texto = "nueva", EsAutomatica = false,
        });
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId = 1, Fecha = DateTime.UtcNow.AddMinutes(-10), Texto = "vieja", EsAutomatica = false,
        });
        await _repo.ActualizarAsync(tarea);
        Context.ChangeTracker.Clear();

        var encontrada = await _repo.ObtenerPorIdAsync(id);

        Assert.Equal(2, encontrada!.Notas.Count);
        Assert.Equal("vieja", encontrada.Notas[0].Texto);
        Assert.Equal("nueva", encontrada.Notas[1].Texto);
    }

    [Fact]
    public async Task ActualizarAsync_PersisteElNuevoEstado()
    {
        // Desvío deliberado del brief: el snippet original seteaba TomadaPorUsuarioId = 1
        // sin sembrar ningún Usuario. AppDbContext mapea TomadaPorUsuarioId con FK Restrict
        // hacia Usuarios, así que ese SaveChanges fallaba con violación de FK (23503) —
        // no es un bug de TareaRepository, es un Arrange incompleto del propio test. Se
        // siembra el Usuario acá, mismo patrón que el resto del proyecto (ver
        // MovimientoStockRepositoryTests.SeedBaseAsync).
        var usuario = NuevoUsuario();
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var tarea = NuevaTarea();
        var id = await _repo.AgregarAsync(tarea);

        tarea.CambiarEstado(EstadoTarea.EnCurso);
        tarea.TomadaPorUsuarioId = usuario.Id;
        tarea.FechaInicio = DateTime.UtcNow;
        await _repo.ActualizarAsync(tarea);
        Context.ChangeTracker.Clear();

        var encontrada = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal(EstadoTarea.EnCurso, encontrada!.Estado);
        Assert.Equal(usuario.Id, encontrada.TomadaPorUsuarioId);
    }

    [Fact]
    public async Task AgregarAsync_ConFechaLimiteUtc_PersisteYSeMantieneAlReleerla()
    {
        // Fix (review final, Important): ningún test de ninguna capa persistía FechaLimite
        // antes de este fix, así que el rechazo real de Npgsql (timestamptz exige
        // DateTimeKind Utc o Unspecified, no Local) quedaba completamente sin cubrir.
        var tarea = NuevaTarea();
        tarea.FechaLimite = DateTime.SpecifyKind(new DateTime(2026, 8, 15), DateTimeKind.Utc);

        var id = await _repo.AgregarAsync(tarea);
        Context.ChangeTracker.Clear();

        var encontrada = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(encontrada!.FechaLimite);
        Assert.Equal(new DateTime(2026, 8, 15), encontrada.FechaLimite!.Value.Date);
    }
}
