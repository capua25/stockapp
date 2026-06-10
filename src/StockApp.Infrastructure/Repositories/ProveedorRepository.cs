using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class ProveedorRepository : IProveedorRepository
{
    private readonly AppDbContext _ctx;

    public ProveedorRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<Proveedor?> ObtenerPorIdAsync(int id)
        => _ctx.Proveedores.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<Proveedor>> ListarTodosAsync()
        => await _ctx.Proveedores.OrderBy(p => p.Nombre).ToListAsync();

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.Proveedores.AnyAsync(p => p.Nombre == nombre && p.Id != excluyendoId.Value)
            : _ctx.Proveedores.AnyAsync(p => p.Nombre == nombre);

    public async Task<int> AgregarAsync(Proveedor proveedor)
    {
        _ctx.Proveedores.Add(proveedor);
        await _ctx.SaveChangesAsync();
        return proveedor.Id;
    }

    public Task ActualizarAsync(Proveedor proveedor)
    {
        _ctx.Proveedores.Update(proveedor);
        return _ctx.SaveChangesAsync();
    }
}
