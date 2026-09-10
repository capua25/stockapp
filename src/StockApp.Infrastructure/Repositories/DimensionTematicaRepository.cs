using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class DimensionTematicaRepository : IDimensionTematicaRepository
{
    private readonly AppDbContext _ctx;

    public DimensionTematicaRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<DimensionTematica?> ObtenerPorIdAsync(int id)
        => _ctx.DimensionesTematicas.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync()
        => await _ctx.DimensionesTematicas.OrderBy(d => d.Nombre).ToListAsync();

    // Comparación case-insensitive vía LOWER() (no ILIKE: ILIKE trata % y _ como comodines,
    // lo cual da falsos positivos en nombres de catálogo que los contengan, y además no puede
    // usar el índice funcional sobre LOWER("Nombre") — ver migración AgregaIndiceFuncionalNombreCatalogos).
    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
    {
        var normalizado = nombre.Trim().ToLower();
        return excluyendoId.HasValue
            ? _ctx.DimensionesTematicas.AnyAsync(d => d.Nombre.ToLower() == normalizado && d.Id != excluyendoId.Value)
            : _ctx.DimensionesTematicas.AnyAsync(d => d.Nombre.ToLower() == normalizado);
    }

    public async Task<int> AgregarAsync(DimensionTematica dimension)
    {
        _ctx.DimensionesTematicas.Add(dimension);
        await _ctx.SaveChangesAsync();
        return dimension.Id;
    }

    public Task ActualizarAsync(DimensionTematica dimension)
    {
        _ctx.DimensionesTematicas.Update(dimension);
        return _ctx.SaveChangesAsync();
    }
}
