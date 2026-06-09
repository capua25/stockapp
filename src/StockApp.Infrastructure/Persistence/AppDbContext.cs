using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;

namespace StockApp.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<UnidadMedida> UnidadesMedida => Set<UnidadMedida>();
    public DbSet<MovimientoStock> MovimientosStock => Set<MovimientoStock>();
    public DbSet<LogAuditoria> LogsAuditoria => Set<LogAuditoria>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Usuario ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasIndex(u => u.NombreUsuario).IsUnique();
            e.Property(u => u.NombreUsuario).IsRequired().HasMaxLength(100);
            e.Property(u => u.HashContrasena).IsRequired();
            e.Property(u => u.Activo).HasDefaultValue(true);
        });

        // ── Producto ──────────────────────────────────────────────────────────
        // CodigoBarras: índice único filtrado para no prohibir múltiples NULLs
        modelBuilder.Entity<Producto>(e =>
        {
            e.HasIndex(p => p.Codigo).IsUnique();
            e.HasIndex(p => p.CodigoBarras).IsUnique()
                .HasFilter("[CodigoBarras] IS NOT NULL");
            e.Property(p => p.Codigo).IsRequired().HasMaxLength(50);
            e.Property(p => p.Nombre).IsRequired();
            e.Property(p => p.PrecioCosto).HasPrecision(18, 4);
            e.Property(p => p.PrecioVenta).HasPrecision(18, 4);
            e.Property(p => p.StockActual).HasPrecision(18, 4);
            e.Property(p => p.StockMinimo).HasPrecision(18, 4).HasDefaultValue(0m);
            e.Property(p => p.Activo).HasDefaultValue(true);
            e.HasOne(p => p.Categoria).WithMany()
                .HasForeignKey(p => p.CategoriaId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(p => p.Proveedor).WithMany()
                .HasForeignKey(p => p.ProveedorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(p => p.UnidadMedida).WithMany()
                .HasForeignKey(p => p.UnidadMedidaId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Categoria ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Categoria>(e =>
        {
            e.Property(c => c.Nombre).IsRequired();
            e.HasIndex(c => c.Nombre).IsUnique();
            e.Property(c => c.Activo).HasDefaultValue(true);
        });

        // ── Proveedor ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Proveedor>(e =>
        {
            e.Property(p => p.Nombre).IsRequired();
            e.HasIndex(p => p.Nombre).IsUnique();
            e.Property(p => p.Activo).HasDefaultValue(true);
        });

        // ── UnidadMedida ──────────────────────────────────────────────────────
        modelBuilder.Entity<UnidadMedida>(e =>
        {
            e.Property(u => u.Nombre).IsRequired();
            e.Property(u => u.Abreviatura).IsRequired().HasMaxLength(10);
            e.HasIndex(u => u.Nombre).IsUnique();
            e.HasIndex(u => u.Abreviatura).IsUnique();
            e.Property(u => u.Activo).HasDefaultValue(true);
        });

        // ── MovimientoStock ───────────────────────────────────────────────────
        // DeleteBehavior.Restrict porque Producto/Usuario usan baja lógica (Activo)
        modelBuilder.Entity<MovimientoStock>(e =>
        {
            e.Property(m => m.Cantidad).HasPrecision(18, 4);
            e.Property(m => m.PrecioUnitario).HasPrecision(18, 4);
            e.HasOne(m => m.Producto).WithMany()
                .HasForeignKey(m => m.ProductoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Usuario).WithMany()
                .HasForeignKey(m => m.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── LogAuditoria ──────────────────────────────────────────────────────
        modelBuilder.Entity<LogAuditoria>(e =>
        {
            e.Property(l => l.Entidad).IsRequired();
            e.Property(l => l.Detalle).IsRequired();
            e.HasOne(l => l.Usuario).WithMany()
                .HasForeignKey(l => l.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
