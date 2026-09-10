using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class OrigenFinanciamientoRepository : IOrigenFinanciamientoRepository
{
    private readonly AppDbContext _ctx;

    public OrigenFinanciamientoRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<OrigenFinanciamiento?> ObtenerPorIdAsync(int id)
        => _ctx.OrigenesFinanciamiento.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync()
        => await _ctx.OrigenesFinanciamiento.OrderBy(o => o.Nombre).ToListAsync();

    // Comparación case-insensitive vía LOWER() (no ILIKE: ILIKE trata % y _ como comodines,
    // lo cual da falsos positivos en nombres de catálogo que los contengan, y además no puede
    // usar el índice funcional sobre LOWER("Nombre") — ver migración AgregaIndiceFuncionalNombreCatalogos).
    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
    {
        var normalizado = nombre.Trim().ToLower();
        return excluyendoId.HasValue
            ? _ctx.OrigenesFinanciamiento.AnyAsync(o => o.Nombre.ToLower() == normalizado && o.Id != excluyendoId.Value)
            : _ctx.OrigenesFinanciamiento.AnyAsync(o => o.Nombre.ToLower() == normalizado);
    }

    public async Task<int> AgregarAsync(OrigenFinanciamiento origen)
    {
        _ctx.OrigenesFinanciamiento.Add(origen);
        await _ctx.SaveChangesAsync();
        return origen.Id;
    }

    public Task ActualizarAsync(OrigenFinanciamiento origen)
    {
        _ctx.OrigenesFinanciamiento.Update(origen);
        return _ctx.SaveChangesAsync();
    }
}
