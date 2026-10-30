using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class ProyectoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly ProyectoRepository _repo;

    public ProyectoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ProyectoRepository(Context);
    }

    private async Task<int> SeedUsuarioAsync()
    {
        var u = new Usuario
        {
            NombreUsuario = $"u{Guid.NewGuid():N}", HashContrasena = "x",
            Rol = RolUsuario.Operador, FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(u);
        await Context.SaveChangesAsync();
        return u.Id;
    }

    private static Proyecto NuevoProyecto(int usuarioId, string titulo = "Plaza") => new()
    {
        Titulo = titulo, CreadoPorUsuarioId = usuarioId, FechaCreacion = DateTime.UtcNow,
    };

    [Fact]
    public async Task AgregarYObtener_ConservaPresupuestoYClasificadores()
    {
        var usuario = await SeedUsuarioAsync();
        var zona = new Zona { Nombre = $"Z{Guid.NewGuid():N}" };
        var dim = new DimensionTematica { Nombre = $"D{Guid.NewGuid():N}" };
        var org = new OrganismoResponsable { Nombre = $"O{Guid.NewGuid():N}" };
        var ori = new OrigenFinanciamiento { Nombre = $"F{Guid.NewGuid():N}" };
        Context.AddRange(zona, dim, org, ori);
        await Context.SaveChangesAsync();

        var p = NuevoProyecto(usuario);
        p.Presupuesto = 1234567.8901m;
        p.ZonaId = zona.Id;
        p.DimensionTematicaId = dim.Id;
        p.OrganismoResponsableId = org.Id;
        p.OrigenFinanciamientoId = ori.Id;
        var id = await _repo.AgregarAsync(p);
        Context.ChangeTracker.Clear();

        var leido = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(leido);
        Assert.Equal(1234567.8901m, leido!.Presupuesto);
        Assert.Equal(zona.Nombre, leido.Zona!.Nombre);
        Assert.Equal(dim.Nombre, leido.DimensionTematica!.Nombre);
        Assert.Equal(org.Nombre, leido.OrganismoResponsable!.Nombre);
        Assert.Equal(ori.Nombre, leido.OrigenFinanciamiento!.Nombre);
    }

    [Fact]
    public async Task ListarGastosActivosAsync_ExcluyeAnuladosYDeOtrosProyectos()
    {
        var usuario = await SeedUsuarioAsync();
        var proveedor = new Proveedor { Nombre = $"P{Guid.NewGuid():N}" };
        var rubro = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "R" };
        Context.AddRange(proveedor, rubro);
        await Context.SaveChangesAsync();
        var a = await _repo.AgregarAsync(NuevoProyecto(usuario, "A"));
        var b = await _repo.AgregarAsync(NuevoProyecto(usuario, "B"));

        Gasto G(int? proyecto, bool activo, DateTime fecha) => new()
        {
            ProveedorId = proveedor.Id, RubroGastoId = rubro.Id, Detalle = "g", Fecha = fecha,
            MontoTotal = 10m, CondicionPago = CondicionPago.Credito, FechaVencimiento = fecha.AddDays(30),
            ProyectoId = proyecto, Activo = activo,
        };
        var viejo = DateTime.UtcNow.AddDays(-5);
        var nuevo = DateTime.UtcNow.AddDays(-1);
        Context.Gastos.AddRange(G(a, true, viejo), G(a, true, nuevo), G(a, false, nuevo), G(b, true, nuevo), G(null, true, nuevo));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var gastos = await _repo.ListarGastosActivosAsync(a);

        Assert.Equal(2, gastos.Count);
        Assert.All(gastos, g => Assert.True(g.Activo && g.ProyectoId == a));
        Assert.True(gastos[0].Fecha > gastos[1].Fecha);
        Assert.All(gastos, g => Assert.NotNull(g.Proveedor));
    }

    [Fact]
    public async Task ActualizarAsync_PersisteNotaNueva()
    {
        var usuario = await SeedUsuarioAsync();
        var id = await _repo.AgregarAsync(NuevoProyecto(usuario));
        Context.ChangeTracker.Clear();

        var p = (await _repo.ObtenerPorIdAsync(id))!;
        p.Notas.Add(new NotaProyecto { UsuarioId = usuario, Fecha = DateTime.UtcNow, Texto = "avance" });
        await _repo.ActualizarAsync(p);
        Context.ChangeTracker.Clear();

        var leido = (await _repo.ObtenerPorIdAsync(id))!;
        var nota = Assert.Single(leido.Notas);
        Assert.Equal("avance", nota.Texto);
        Assert.Equal(id, nota.ProyectoId);
    }
}
