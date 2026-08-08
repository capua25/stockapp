using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Migrations;

/// <summary>
/// Guardián del BLOQUEANTE 1+2 del review adversarial (fix/integridad-referencial): la migración
/// 20260808125605_AgregaFksLotesImportacion agrega 4 FK reales (Gastos/IngresosCaja/LineasPoa/
/// PagosGasto → LotesImportacion) SIN backfill era el bug -- reproducido empíricamente contra un
/// Postgres limpio antes del fix: migrar hasta 20260806170927, insertar un Gasto con IdImportacion
/// poblado (columna que existe desde 20260721153410, mucho antes que LotesImportacion) y correr
/// `database update` daba 23503 (violates foreign key constraint). Como Program.cs corre
/// MigrateAsync() al arranque, eso es un crash-loop de la API entera.
///
/// Este test NO usa PostgresFixture.CrearContexto() (que ya migra a HEAD antes de que este test
/// pueda sembrar nada en un punto intermedio) -- crea una base NUEVA en el MISMO contenedor
/// Postgres ya levantado por la colección "Postgres" (CREATE DATABASE, no un contenedor
/// Testcontainers nuevo) para poder controlar el punto exacto de la migración. Evita así
/// duplicar el costo/riesgo de un segundo contenedor+Ryuk (ver LockInicializacionContenedores).
/// </summary>
[Collection("Postgres")]
public sealed class BackfillLotesImportacionMigrationTests
{
    private const string MigracionAntesDelBackfill = "20260806170927_CorrigePagoGastoEsAutomaticoImportacion";
    private const string MigracionAntesDeLotesImportacion = "20260808123448_AgregaFksIntegridadReferencialTareas";
    private const int AccionImportacionPlanillas = 42;
    private const int AccionReversionImportacion = 43;

    private readonly PostgresFixture _fixture;

    public BackfillLotesImportacionMigrationTests(PostgresFixture fixture) => _fixture = fixture;

    /// <summary>Crea una base nueva y vacía en el mismo servidor Postgres del fixture compartido,
    /// y devuelve su connection string. Un nombre por test (Guid) evita colisiones entre tests que
    /// corren en la misma collection.</summary>
    private async Task<string> CrearBaseVaciaAsync()
    {
        var nombreBase = "migtest_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{nombreBase}\";";
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = nombreBase };
        return builder.ConnectionString;
    }

    private static AppDbContext CrearContexto(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Backfill_SinFix_Falla23503_ConFix_ReconstruyeYPermiteLasFks()
    {
        var connectionString = await CrearBaseVaciaAsync();

        // ── 1. Migrar sólo hasta ANTES de LotesImportacion/FKs ──────────────────────────────
        await using (var ctx = CrearContexto(connectionString))
        {
            var migrador = ctx.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrador.MigrateAsync(MigracionAntesDelBackfill);
        }

        // ── 2. Sembrar datos reales con IdImportacion poblado + sus LogsAuditoria ───────────
        Guid idLoteNormal;
        Guid idLoteRevertido;
        Guid idLoteHuerfano;
        int usuarioId;
        DateTime fechaConfirmacionRevertido;
        await using (var ctx = CrearContexto(connectionString))
        {
            var usuario = new Usuario
            {
                NombreUsuario = "backfill-tests",
                HashContrasena = "hash",
                Rol = RolUsuario.Admin,
                Activo = true,
                FechaAlta = DateTime.UtcNow,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            usuarioId = usuario.Id;

            var proveedor = new Proveedor { Nombre = "Proveedor Backfill" };
            var fuente = new FuenteFinanciamiento { Nombre = "Fuente Backfill" };
            var rubro = new RubroGasto { Codigo = 1, Nombre = "Rubro Backfill" };
            ctx.Proveedores.Add(proveedor);
            ctx.FuentesFinanciamiento.Add(fuente);
            ctx.RubrosGasto.Add(rubro);
            await ctx.SaveChangesAsync();

            // Lote NORMAL (no revertido): Gasto + Ingreso + Pago con el mismo Guid, y su
            // LogAuditoria de confirmación (Accion=42, EntidadId=Ejercicio -- mismo criterio que
            // ImportacionRepository.ConfirmarAsync escribe hoy).
            idLoteNormal = Guid.NewGuid();
            var fechaLoteNormal = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
            ctx.Gastos.Add(new Gasto
            {
                Proveedor = proveedor, Detalle = "Gasto del lote normal", Fecha = fechaLoteNormal,
                MontoTotal = 1000m, FuenteFinanciamiento = fuente, RubroGasto = rubro,
                CondicionPago = CondicionPago.Contado, IdImportacion = idLoteNormal,
            });
            ctx.IngresosCaja.Add(new IngresoCaja
            {
                Fecha = fechaLoteNormal, Concepto = "Ingreso del lote normal", Monto = 500m,
                FuenteFinanciamiento = fuente, IdImportacion = idLoteNormal,
            });
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = usuarioId, Fecha = fechaLoteNormal, Accion = (AccionAuditada)AccionImportacionPlanillas,
                Entidad = "Importacion", EntidadId = 2026, IdLote = idLoteNormal, Detalle = "Confirmación lote normal",
            });

            // Lote REVERTIDO: mismo patrón + LogAuditoria de reversa (Accion=43).
            idLoteRevertido = Guid.NewGuid();
            fechaConfirmacionRevertido = new DateTime(2026, 2, 5, 9, 0, 0, DateTimeKind.Utc);
            var fechaReversion = new DateTime(2026, 2, 6, 9, 0, 0, DateTimeKind.Utc);
            ctx.Gastos.Add(new Gasto
            {
                Proveedor = proveedor, Detalle = "Gasto del lote revertido", Fecha = fechaConfirmacionRevertido,
                MontoTotal = 2000m, FuenteFinanciamiento = fuente, RubroGasto = rubro,
                CondicionPago = CondicionPago.Contado, IdImportacion = idLoteRevertido,
            });
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = usuarioId, Fecha = fechaConfirmacionRevertido, Accion = (AccionAuditada)AccionImportacionPlanillas,
                Entidad = "Importacion", EntidadId = 2025, IdLote = idLoteRevertido, Detalle = "Confirmación lote revertido",
            });
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = usuarioId, Fecha = fechaReversion, Accion = (AccionAuditada)AccionReversionImportacion,
                Entidad = "Importacion", EntidadId = 2025, IdLote = idLoteRevertido, Detalle = "Reversa lote revertido",
            });

            // Lote HUÉRFANO (el "caso difícil" del review): tiene un Gasto real con IdImportacion
            // poblado, pero NINGÚN LogAuditoria de origen (log borrado, o dato migrado a mano).
            // Antes del fix, este es exactamente el Guid que hace explotar el AddForeignKey de
            // Gastos con 23503 -- no aparece en LotesImportacion (que ni siquiera existe todavía
            // en este punto de la migración) y no hay forma de derivar autor/ejercicio.
            idLoteHuerfano = Guid.NewGuid();
            var fechaGastoHuerfano = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
            ctx.Gastos.Add(new Gasto
            {
                Proveedor = proveedor, Detalle = "Gasto huérfano sin log", Fecha = fechaGastoHuerfano,
                MontoTotal = 300m, FuenteFinanciamiento = fuente, RubroGasto = rubro,
                CondicionPago = CondicionPago.Contado, IdImportacion = idLoteHuerfano,
            });

            await ctx.SaveChangesAsync();
        }

        // ── 3. Migrar hasta HEAD: acá es donde, SIN el fix, tira 23503 ──────────────────────
        await using (var ctx = CrearContexto(connectionString))
        {
            var migrador = ctx.GetInfrastructure().GetRequiredService<IMigrator>();

            // No debe fallar -- este Assert.NotNull (en vez de simplemente `await migrador...`)
            // hace que, si algo tira, xUnit reporte la excepción real (23503 si el backfill se
            // rompiera) en vez de un fallo genérico de setup.
            var excepcion = await Record.ExceptionAsync(() => migrador.MigrateAsync());
            Assert.Null(excepcion);
        }

        // ── 4. Verificar reconstrucción ──────────────────────────────────────────────────────
        await using (var ctx = CrearContexto(connectionString))
        {
            var lotes = await ctx.LotesImportacion.ToDictionaryAsync(l => l.Id);

            Assert.True(lotes.ContainsKey(idLoteNormal));
            Assert.Equal(usuarioId, lotes[idLoteNormal].UsuarioId);
            Assert.Equal(2026, lotes[idLoteNormal].Ejercicio);
            Assert.Null(lotes[idLoteNormal].RevertidaEn);

            Assert.True(lotes.ContainsKey(idLoteRevertido));
            Assert.Equal(usuarioId, lotes[idLoteRevertido].UsuarioId);
            Assert.Equal(2025, lotes[idLoteRevertido].Ejercicio);
            Assert.NotNull(lotes[idLoteRevertido].RevertidaEn);
            Assert.Equal(usuarioId, lotes[idLoteRevertido].RevertidaPorUsuarioId);

            // Caso difícil: reconstruido con autoría/ejercicio desconocidos (decisión del
            // usuario), nunca invisible ni bloqueando la migración.
            Assert.True(lotes.ContainsKey(idLoteHuerfano));
            Assert.Null(lotes[idLoteHuerfano].UsuarioId);
            Assert.Null(lotes[idLoteHuerfano].Ejercicio);
            Assert.Null(lotes[idLoteHuerfano].RevertidaEn);

            // ListarHistorialAsync (Bloqueante 2: antes de este fix, un lote sin fila en
            // LotesImportacion desaparecía en silencio de este historial) tiene que devolver los 3.
            var repo = new ImportacionRepository(ctx);
            var historial = await repo.ListarHistorialAsync();
            var idsEnHistorial = historial.Select(h => h.IdImportacion).ToHashSet();

            Assert.Contains(idLoteNormal, idsEnHistorial);
            Assert.Contains(idLoteRevertido, idsEnHistorial);
            Assert.Contains(idLoteHuerfano, idsEnHistorial);

            var filaHuerfana = historial.Single(h => h.IdImportacion == idLoteHuerfano);
            Assert.Equal(0, filaHuerfana.Ejercicio); // sentinel documentado (ver ImportacionRepository)
            Assert.False(filaHuerfana.Revertida);

            var filaRevertida = historial.Single(h => h.IdImportacion == idLoteRevertido);
            Assert.True(filaRevertida.Revertida);
        }
    }

    /// <summary>
    /// IMPORTANTE 5 del review adversarial: Down() de AgregaLotesImportacion hace DropTable de
    /// LotesImportacion -- formalmente simétrico, pero borra la única fuente de verdad de "esta
    /// importación está revertida". Este test verifica que un Down()+Up() completo (que pasa por
    /// ese DropTable) se AUTO-REPARA solo: el backfill de AgregaFksLotesImportacion reconstruye
    /// TODO de nuevo desde LogsAuditoria/Gastos/IngresosCaja/LineasPoa/PagosGasto, que ese Down()
    /// nunca toca.
    /// </summary>
    [Fact]
    public async Task DownYUpDeAgregaLotesImportacion_SeAutoRepara()
    {
        var connectionString = await CrearBaseVaciaAsync();
        Guid idLote;
        int usuarioId;

        await using (var ctx = CrearContexto(connectionString))
        {
            var migrador = ctx.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrador.MigrateAsync(MigracionAntesDelBackfill);
        }

        await using (var ctx = CrearContexto(connectionString))
        {
            var usuario = new Usuario
            {
                NombreUsuario = "backfill-down-up",
                HashContrasena = "hash",
                Rol = RolUsuario.Admin,
                Activo = true,
                FechaAlta = DateTime.UtcNow,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            usuarioId = usuario.Id;

            var proveedor = new Proveedor { Nombre = "Proveedor Down/Up" };
            var fuente = new FuenteFinanciamiento { Nombre = "Fuente Down/Up" };
            var rubro = new RubroGasto { Codigo = 2, Nombre = "Rubro Down/Up" };
            ctx.Proveedores.Add(proveedor);
            ctx.FuentesFinanciamiento.Add(fuente);
            ctx.RubrosGasto.Add(rubro);
            await ctx.SaveChangesAsync();

            idLote = Guid.NewGuid();
            var fecha = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
            ctx.Gastos.Add(new Gasto
            {
                Proveedor = proveedor, Detalle = "Gasto Down/Up", Fecha = fecha, MontoTotal = 700m,
                FuenteFinanciamiento = fuente, RubroGasto = rubro, CondicionPago = CondicionPago.Contado,
                IdImportacion = idLote,
            });
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = usuarioId, Fecha = fecha, Accion = (AccionAuditada)AccionImportacionPlanillas,
                Entidad = "Importacion", EntidadId = 2024, IdLote = idLote, Detalle = "Confirmación Down/Up",
            });

            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CrearContexto(connectionString))
        {
            var migrador = ctx.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrador.MigrateAsync(); // primera pasada a HEAD

            var loteAntes = await ctx.LotesImportacion.SingleAsync(l => l.Id == idLote);
            Assert.Equal(usuarioId, loteAntes.UsuarioId);
            Assert.Equal(2024, loteAntes.Ejercicio);
        }

        // Down() completo (dropea FKs y la tabla LotesImportacion) + Up() de nuevo.
        await using (var ctx = CrearContexto(connectionString))
        {
            var migrador = ctx.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrador.MigrateAsync(MigracionAntesDeLotesImportacion);

            var excepcion = await Record.ExceptionAsync(() => migrador.MigrateAsync());
            Assert.Null(excepcion);
        }

        await using (var ctx = CrearContexto(connectionString))
        {
            var loteDespues = await ctx.LotesImportacion.SingleAsync(l => l.Id == idLote);
            Assert.Equal(usuarioId, loteDespues.UsuarioId);
            Assert.Equal(2024, loteDespues.Ejercicio);
            Assert.Null(loteDespues.RevertidaEn);
        }
    }
}
