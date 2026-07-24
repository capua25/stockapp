using Microsoft.EntityFrameworkCore;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>F5d Task 1: ListarHistorialAsync lee ÚNICAMENTE de LogsAuditoria, sin entidad
/// cabecera. Reusa el mismo seed de usuario que ImportacionRepositoryTests.</summary>
public class ImportacionRepositoryHistorialTests : PostgresRepositoryTestBase
{
    private const int Ejercicio = 2026;
    private readonly ImportacionRepository _repo;
    private readonly int _usuarioId;

    public ImportacionRepositoryHistorialTests(PostgresFixture fixture) : base(fixture)
    {
        var usuarioSemilla = new Usuario
        {
            NombreUsuario = "historial-tests",
            HashContrasena = "hash",
            Rol = RolUsuario.Admin,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuarioSemilla);
        Context.SaveChanges();
        _usuarioId = usuarioSemilla.Id;
        Context.ChangeTracker.Clear();

        _repo = new ImportacionRepository(Context);
    }

    private static ConfirmarImportacionDto PayloadMinimo(int ejercicio, bool forzar = false) => new(
        Ejercicio: ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
        Ingresos: new List<IngresoConfirmarDto>(),
        Gastos: new List<GastoConfirmarDto>(),
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task ListarHistorialAsync_ImportacionSinRevertir_LaListaComoActiva()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadMinimo(Ejercicio), usuarioId: _usuarioId);

        var historial = await _repo.ListarHistorialAsync();

        var fila = Assert.Single(historial);
        Assert.Equal(resultado.IdImportacion, fila.IdImportacion);
        Assert.Equal(Ejercicio, fila.Ejercicio);
        Assert.Equal("historial-tests", fila.Usuario);
        Assert.False(fila.Revertida);
    }

    [Fact]
    public async Task ListarHistorialAsync_ImportacionRevertida_LaMarcaComoRevertida()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadMinimo(Ejercicio), usuarioId: _usuarioId);
        await _repo.RevertirAsync(resultado.IdImportacion, usuarioId: _usuarioId);

        var historial = await _repo.ListarHistorialAsync();

        var fila = Assert.Single(historial);
        Assert.True(fila.Revertida);
    }

    [Fact]
    public async Task ListarHistorialAsync_VariasImportaciones_OrdenaPorFechaDescendente()
    {
        var primero = await _repo.ConfirmarAsync(PayloadMinimo(2024), usuarioId: _usuarioId);
        var segundo = await _repo.ConfirmarAsync(PayloadMinimo(2025), usuarioId: _usuarioId);

        var historial = await _repo.ListarHistorialAsync();

        Assert.Equal(2, historial.Count);
        Assert.Equal(segundo.IdImportacion, historial[0].IdImportacion);
        Assert.Equal(primero.IdImportacion, historial[1].IdImportacion);
    }
}
