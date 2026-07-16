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
    public DbSet<FuenteFinanciamiento> FuentesFinanciamiento => Set<FuenteFinanciamiento>();
    public DbSet<RubroGasto> RubrosGasto => Set<RubroGasto>();
    public DbSet<LineaPoa> LineasPoa => Set<LineaPoa>();
    public DbSet<AsignacionPresupuestal> AsignacionesPresupuestales => Set<AsignacionPresupuestal>();
    public DbSet<Gasto> Gastos => Set<Gasto>();
    public DbSet<PagoGasto> PagosGasto => Set<PagoGasto>();
    public DbSet<IngresoCaja> IngresosCaja => Set<IngresoCaja>();

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
                .HasFilter("\"CodigoBarras\" IS NOT NULL");
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
            // Índice compuesto para acelerar historial por producto+fecha (PA-04)
            e.HasIndex(m => new { m.ProductoId, m.Fecha })
             .HasDatabaseName("IX_MovimientosStock_ProductoId_Fecha");
        });

        // ── LogAuditoria ──────────────────────────────────────────────────────
        modelBuilder.Entity<LogAuditoria>(e =>
        {
            e.Property(l => l.Entidad).IsRequired();
            e.Property(l => l.Detalle).IsRequired();
            e.HasOne(l => l.Usuario).WithMany()
                .HasForeignKey(l => l.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Finanzas: maestros (Fase 1 módulo Finanzas) ───────────────────────
        modelBuilder.Entity<FuenteFinanciamiento>(e =>
        {
            e.Property(f => f.Nombre).IsRequired();
            e.HasIndex(f => f.Nombre).IsUnique();
            e.Property(f => f.Activo).HasDefaultValue(true);
        });

        modelBuilder.Entity<RubroGasto>(e =>
        {
            e.HasIndex(r => r.Codigo).IsUnique();
            e.Property(r => r.Nombre).IsRequired();
            e.Property(r => r.Activo).HasDefaultValue(true);
        });

        modelBuilder.Entity<LineaPoa>(e =>
        {
            e.Property(l => l.Nombre).IsRequired();
            e.Property(l => l.Programa).IsRequired();
            e.HasIndex(l => new { l.Nombre, l.Ejercicio }).IsUnique();
            e.Property(l => l.Activo).HasDefaultValue(true);
        });

        // AsignacionPresupuestal: hija del agregado LineaPoa. FKs Restrict porque los
        // maestros usan baja lógica (nunca se borra una LineaPoa o Fuente físicamente);
        // el reemplazo de asignaciones es un delete explícito en el repo, que Restrict
        // NO impide (Restrict solo bloquea cascadas desde el padre).
        modelBuilder.Entity<AsignacionPresupuestal>(e =>
        {
            e.Property(a => a.Monto).HasPrecision(18, 4);
            e.HasIndex(a => new { a.LineaPoaId, a.FuenteFinanciamientoId }).IsUnique();
            e.HasOne<LineaPoa>().WithMany(l => l.Asignaciones)
                .HasForeignKey(a => a.LineaPoaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.FuenteFinanciamiento).WithMany()
                .HasForeignKey(a => a.FuenteFinanciamientoId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Finanzas: documentos (Fase 2 módulo Finanzas) ─────────────────────
        // FKs Restrict en todos lados: los maestros y los gastos usan baja lógica,
        // nunca DELETE físico — no hay cascadas que propagar.
        modelBuilder.Entity<Gasto>(e =>
        {
            e.Property(g => g.Detalle).IsRequired();
            e.Property(g => g.MontoTotal).HasPrecision(18, 4);
            e.Property(g => g.Activo).HasDefaultValue(true);
            e.HasIndex(g => g.Fecha);
            // No único: la unicidad proveedor+factura es regla de negocio SOLO entre
            // gastos activos (un gasto anulado libera su número de factura).
            e.HasIndex(g => new { g.ProveedorId, g.NumeroFactura });
            e.HasOne(g => g.Proveedor).WithMany()
                .HasForeignKey(g => g.ProveedorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.FuenteFinanciamiento).WithMany()
                .HasForeignKey(g => g.FuenteFinanciamientoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.RubroGasto).WithMany()
                .HasForeignKey(g => g.RubroGastoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.LineaPoa).WithMany()
                .HasForeignKey(g => g.LineaPoaId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PagoGasto>(e =>
        {
            e.Property(p => p.Monto).HasPrecision(18, 4);
            e.Property(p => p.Activo).HasDefaultValue(true);
            e.HasIndex(p => p.GastoId);
            e.HasOne<Gasto>().WithMany(g => g.Pagos)
                .HasForeignKey(p => p.GastoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IngresoCaja>(e =>
        {
            e.Property(i => i.Concepto).IsRequired();
            e.Property(i => i.Monto).HasPrecision(18, 4);
            e.Property(i => i.Activo).HasDefaultValue(true);
            e.HasIndex(i => i.Fecha);
            e.HasOne(i => i.FuenteFinanciamiento).WithMany()
                .HasForeignKey(i => i.FuenteFinanciamientoId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Vínculo stock ↔ finanzas: FK opcional en MovimientoStock ─────────
        modelBuilder.Entity<MovimientoStock>(e =>
        {
            e.HasIndex(m => m.GastoId);
            e.HasOne(m => m.Gasto).WithMany()
                .HasForeignKey(m => m.GastoId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
