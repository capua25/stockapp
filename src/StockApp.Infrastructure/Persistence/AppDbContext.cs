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
    public DbSet<Adjunto> Adjuntos => Set<Adjunto>();
    public DbSet<AdjuntoContenido> AdjuntosContenido => Set<AdjuntoContenido>();
    public DbSet<IngresoCaja> IngresosCaja => Set<IngresoCaja>();
    public DbSet<CorridaBackup> CorridasBackup => Set<CorridaBackup>();
    public DbSet<ConfiguracionAlertas> ConfiguracionesAlertas => Set<ConfiguracionAlertas>();
    public DbSet<Tarea> Tareas => Set<Tarea>();
    public DbSet<NotaTarea> NotasTarea => Set<NotaTarea>();
    public DbSet<LoteImportacion> LotesImportacion => Set<LoteImportacion>();

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
            e.HasIndex(l => l.IdLote);
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
            e.HasIndex(l => l.IdImportacion);
            e.HasOne<LoteImportacion>().WithMany()
                .HasForeignKey(l => l.IdImportacion).OnDelete(DeleteBehavior.Restrict);
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

        // ── LoteImportacion (fix/integridad-referencial) ──────────────────────
        // Id lo asigna la APP (Guid.NewGuid(), ver LoteImportacion.cs) ANTES del Add() —
        // ValueGeneratedNever() evita que EF/Postgres intenten generar o ignorar ese valor.
        // Sin nav de colección hacia los hijos (Gastos/IngresosCaja/LineasPoa/PagosGasto): nada
        // lee "los gastos de este lote" por navegación, todo el código filtra por el FK escalar
        // IdImportacion == idLote (mismo criterio ya usado en ImportacionRepository) — mismo
        // patrón que AsignacionPresupuestal→LineaPoa y NotaTarea→Tarea (relación configurada
        // solo del lado necesario, sin agregar superficie que nadie consume).
        //
        // Índice sobre Ejercicio: NO único. Un único filtrado por RevertidaEn IS NULL fue
        // evaluado y descartado a propósito — ConfirmarAsync permite reimportar un ejercicio con
        // un lote previo SIN revertir si dto.Forzar == true (ver BuscarImportacionNoRevertidaAsync
        // en ImportacionRepository), así que más de un lote sin revertir para el MISMO ejercicio
        // es un estado válido del negocio, no un bug. Un único filtrado convertiría esa
        // reimportación forzada en un 23505 real, rompiendo una función soportada.
        modelBuilder.Entity<LoteImportacion>(e =>
        {
            e.Property(l => l.Id).ValueGeneratedNever();
            e.HasIndex(l => l.Ejercicio);
            e.HasOne(l => l.Usuario).WithMany()
                .HasForeignKey(l => l.UsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Usuario>().WithMany()
                .HasForeignKey(l => l.RevertidaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
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
            // Único PARCIAL: la unicidad proveedor+factura+orden es regla de negocio SOLO
            // entre gastos activos con factura (un gasto anulado libera su número de
            // factura, y los gastos sin factura —compromisos, expedientes— no cuentan).
            // Cierra en BD la carrera que ValidarFacturaUnicaAsync (check-then-act en
            // memoria) no puede cerrar sola: dos altas concurrentes con la misma
            // factura+orden ya no pueden committear ambas. GastoRepository mapea la
            // violación (Npgsql 23505) a ReglaDeNegocioException con el mismo mensaje que
            // la validación de aplicación.
            //
            // NumeroOrden se sumó a la clave (migración AmpliaIndiceFacturaConNumeroOrden,
            // F5c) porque la planilla real 2026 tiene proveedores que reutilizan el mismo
            // número de factura en dos renglones con distinto número de orden (una factura
            // imputada en varias partes) — ver docs/finanzas-facturas-duplicadas-planilla-2026.md.
            // AreNullsDistinct(false) ⇒ NULLS NOT DISTINCT: dos gastos activos del mismo
            // proveedor+factura SIN NumeroOrden siguen colisionando entre sí (Postgres trata
            // NULL como distinto de NULL por defecto; sin este flag, dos gastos así se
            // colarían y el índice quedaría MÁS débil que el viejo, no más preciso).
            e.HasIndex(g => new { g.ProveedorId, g.NumeroFactura, g.NumeroOrden })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasFilter("\"Activo\" = TRUE AND \"NumeroFactura\" IS NOT NULL");
            e.HasOne(g => g.Proveedor).WithMany()
                .HasForeignKey(g => g.ProveedorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.FuenteFinanciamiento).WithMany()
                .HasForeignKey(g => g.FuenteFinanciamientoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.RubroGasto).WithMany()
                .HasForeignKey(g => g.RubroGastoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.LineaPoa).WithMany()
                .HasForeignKey(g => g.LineaPoaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(g => g.IdImportacion);
            // FK real hacia LotesImportacion (fix/integridad-referencial): mapeada como relación
            // EF (HasOne/WithMany), no solo constraint SQL. Sin esto, EF no sabe que el
            // LoteImportacion tiene que insertarse ANTES que este Gasto dentro del único
            // SaveChangesAsync de ConfirmarAsync — el orden de inserción entre tablas sin
            // relación declarada queda indefinido y produciría violaciones de FK intermitentes.
            // Sin nav (WithMany() sin colección): nada necesita "los gastos de este lote" por
            // navegación, todo filtra por el FK escalar IdImportacion (ver AppDbContext.cs,
            // sección LoteImportacion).
            e.HasOne<LoteImportacion>().WithMany()
                .HasForeignKey(g => g.IdImportacion).OnDelete(DeleteBehavior.Restrict);
            // No es una columna real: GastoRepository la proyecta en la misma query (EXISTS
            // correlacionado contra MovimientosStock), nunca se persiste como escritura.
            e.Ignore(g => g.TieneMovimientosDeStock);
        });

        modelBuilder.Entity<PagoGasto>(e =>
        {
            e.Property(p => p.Monto).HasPrecision(18, 4);
            e.Property(p => p.Activo).HasDefaultValue(true);
            e.HasIndex(p => p.GastoId);
            e.HasOne(p => p.Gasto).WithMany(g => g.Pagos)
                .HasForeignKey(p => p.GastoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.IdImportacion);
            // Ver comentario de Gasto.IdImportacion más arriba: misma FK real + misma razón
            // (orden de inserción dentro del único SaveChangesAsync de ConfirmarAsync).
            e.HasOne<LoteImportacion>().WithMany()
                .HasForeignKey(p => p.IdImportacion).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IngresoCaja>(e =>
        {
            e.Property(i => i.Concepto).IsRequired();
            e.Property(i => i.Monto).HasPrecision(18, 4);
            e.Property(i => i.Activo).HasDefaultValue(true);
            e.HasIndex(i => i.Fecha);
            e.HasOne(i => i.FuenteFinanciamiento).WithMany()
                .HasForeignKey(i => i.FuenteFinanciamientoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.IdImportacion);
            e.HasOne<LoteImportacion>().WithMany()
                .HasForeignKey(i => i.IdImportacion).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Finanzas: adjuntos (Fase 3 módulo Finanzas) ───────────────────────
        // Contenido separado en tabla propia (bytea) para que ListarPorGasto/Pago nunca
        // traigan bytes. CHECK XOR en BD como defensa en profundidad (AdjuntoService ya
        // valida la invariante en memoria antes de llegar acá).
        modelBuilder.Entity<Adjunto>(e =>
        {
            e.Property(a => a.NombreArchivo).IsRequired();
            e.Property(a => a.ContentType).IsRequired();
            e.Property(a => a.Activo).HasDefaultValue(true);
            e.HasIndex(a => a.GastoId);
            e.HasIndex(a => a.PagoGastoId);
            e.HasCheckConstraint(
                "CK_Adjuntos_GastoOPago",
                "(\"GastoId\" IS NOT NULL AND \"PagoGastoId\" IS NULL) OR " +
                "(\"GastoId\" IS NULL AND \"PagoGastoId\" IS NOT NULL)");
            e.HasOne(a => a.Gasto).WithMany()
                .HasForeignKey(a => a.GastoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.PagoGasto).WithMany()
                .HasForeignKey(a => a.PagoGastoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AdjuntoContenido>().WithOne()
                .HasForeignKey<AdjuntoContenido>(c => c.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AdjuntoContenido>(e =>
        {
            e.Property(c => c.Contenido).IsRequired();
        });

        // ── Vínculo stock ↔ finanzas: FK opcional en MovimientoStock ─────────
        modelBuilder.Entity<MovimientoStock>(e =>
        {
            e.HasIndex(m => m.GastoId);
            e.HasOne(m => m.Gasto).WithMany()
                .HasForeignKey(m => m.GastoId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Backups programados (Entrega 1 + fix/integridad-referencial) ───────
        // UsuarioId nullable + Restrict, mismo criterio que NotaTarea.Usuario: null es un
        // valor legítimo (job automático o fila reconciliada desde disco), no un hueco de FK.
        modelBuilder.Entity<CorridaBackup>(e =>
        {
            e.HasIndex(c => c.FinalizadaEn);
            e.HasOne(c => c.Usuario).WithMany()
                .HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Configuración del canal de alertas (fila única, Id = 1) ─────────────
        // Mismo patrón de FK que CorridaBackup.Usuario: nullable + Restrict. Null es un valor
        // legítimo (la fila sembrada por la migración nunca la tocó nadie), no un hueco de FK.
        modelBuilder.Entity<ConfiguracionAlertas>(e =>
        {
            e.Property(c => c.Id).ValueGeneratedNever(); // fila única: el Id lo fija el código, no la secuencia
            e.HasOne(c => c.Usuario).WithMany()
                .HasForeignKey(c => c.ActualizadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Tareas (módulo independiente, spec 2026-08-01) ────────────────────
        // CreadaPorUsuarioId/CerradaPorUsuarioId/NotaTarea.UsuarioId (fix/integridad-referencial):
        // las tres apuntaban a Usuarios.Id sin FK. Restrict en las tres, mismo criterio que
        // TomadaPorUsuarioId y el resto del modelo (Usuarios usa baja lógica, nunca DELETE físico).
        modelBuilder.Entity<Tarea>(e =>
        {
            e.Property(t => t.Titulo).IsRequired();
            e.HasIndex(t => t.Estado);
            e.HasOne(t => t.CreadaPor).WithMany()
                .HasForeignKey(t => t.CreadaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.TomadaPor).WithMany()
                .HasForeignKey(t => t.TomadaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.CerradaPor).WithMany()
                .HasForeignKey(t => t.CerradaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NotaTarea>(e =>
        {
            e.Property(n => n.Texto).IsRequired();
            e.HasIndex(n => n.TareaId);
            // Sin nav Tarea en NotaTarea (mismo criterio que AsignacionPresupuestal → LineaPoa):
            // la relación se configura solo del lado padre.
            e.HasOne<Tarea>().WithMany(t => t.Notas)
                .HasForeignKey(n => n.TareaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.Usuario).WithMany()
                .HasForeignKey(n => n.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
