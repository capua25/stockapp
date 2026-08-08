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
    private const string MigracionAgregaLotesImportacion = "20260808125147_AgregaLotesImportacion";
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
        Guid idLoteSoloLineaPoa;
        Guid idLoteSoloPago;
        Guid idLoteEnTodasLasTablas;
        Guid idLoteDobleLogConfirmacion;
        int usuarioId;
        DateTime fechaConfirmacionRevertido;
        DateTime fechaReversionEsperada;
        DateTime fechaPagoHuerfanoEsperada;
        DateTime fechaIngresoTodasEsperada;
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
            fechaReversionEsperada = new DateTime(2026, 2, 6, 9, 0, 0, DateTimeKind.Utc);
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
                UsuarioId = usuarioId, Fecha = fechaReversionEsperada, Accion = (AccionAuditada)AccionReversionImportacion,
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

            // Lote SOLO con LineaPoa (Menor 5 del review): ningún Gasto/Ingreso/Pago, sin
            // LogAuditoria -- ejercita la rama LineasPoa del UNION de "guids", la única fuente
            // real de "ejercicio_derivado" (ed, ya que no hay confirmacion), y el fallback de
            // Fecha a 1970-01-01 (fecha_derivada no incluye LineasPoa: no tiene columna Fecha).
            idLoteSoloLineaPoa = Guid.NewGuid();
            ctx.LineasPoa.Add(new LineaPoa
            {
                Nombre = "Linea huerfana", Programa = "Ambiente", Ejercicio = 2027,
                IdImportacion = idLoteSoloLineaPoa,
            });

            // Lote SOLO con PagoGasto (Menor 5): el Gasto dueño del pago es una carga MANUAL
            // (IdImportacion null) -- solo el PAGO quedó estampado por una corrida cuyo Guid no
            // aparece en ninguna otra tabla. Ejercita la rama PagosGasto del UNION de "guids" y la
            // rama PagosGasto del UNION ALL de fecha_derivada.
            idLoteSoloPago = Guid.NewGuid();
            var gastoParaPagoHuerfano = new Gasto
            {
                Proveedor = proveedor, Detalle = "Gasto manual con pago de lote huérfano",
                Fecha = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                MontoTotal = 400m, FuenteFinanciamiento = fuente, RubroGasto = rubro,
                CondicionPago = CondicionPago.Credito, IdImportacion = null,
            };
            ctx.Gastos.Add(gastoParaPagoHuerfano);
            fechaPagoHuerfanoEsperada = new DateTime(2026, 3, 12, 15, 0, 0, DateTimeKind.Utc);
            ctx.PagosGasto.Add(new PagoGasto
            {
                Gasto = gastoParaPagoHuerfano, Fecha = fechaPagoHuerfanoEsperada, Monto = 400m,
                IdImportacion = idLoteSoloPago,
            });

            // Lote presente en las 4 tablas a la vez, SIN LogAuditoria (Menor 5): prueba que
            // "guids" (UNION, no UNION ALL) no duplica la fila pese a aparecer 4 veces, que
            // fecha_derivada toma el MIN real entre Gasto/Ingreso/Pago (3 fechas distintas), y
            // que el Ejercicio sale de LineasPoa (ed) al no haber confirmacion. Dos LineaPoa con
            // el MISMO IdImportacion (Menor 2): ejercita el ORDER BY explícito de ejercicio_derivado
            // -- sin él, Postgres podía devolver cualquiera de las dos filas.
            idLoteEnTodasLasTablas = Guid.NewGuid();
            var fechaGastoTodas = new DateTime(2026, 6, 5, 8, 0, 0, DateTimeKind.Utc);
            fechaIngresoTodasEsperada = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc); // la más temprana
            var fechaPagoTodas = new DateTime(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc);
            var gastoTodas = new Gasto
            {
                Proveedor = proveedor, Detalle = "Gasto del lote en las 4 tablas", Fecha = fechaGastoTodas,
                MontoTotal = 900m, FuenteFinanciamiento = fuente, RubroGasto = rubro,
                CondicionPago = CondicionPago.Contado, IdImportacion = idLoteEnTodasLasTablas,
            };
            ctx.Gastos.Add(gastoTodas);
            ctx.IngresosCaja.Add(new IngresoCaja
            {
                Fecha = fechaIngresoTodasEsperada, Concepto = "Ingreso del lote en las 4 tablas", Monto = 300m,
                FuenteFinanciamiento = fuente, IdImportacion = idLoteEnTodasLasTablas,
            });
            ctx.LineasPoa.Add(new LineaPoa
            {
                // Primera de las dos -- Id más chico (insertada primero) -- tiene que ser la que
                // gana con el ORDER BY "IdImportacion", "Id" del fix de Menor 2.
                Nombre = "Linea A del lote en las 4 tablas", Programa = "Ambiente", Ejercicio = 2028,
                IdImportacion = idLoteEnTodasLasTablas,
            });
            ctx.LineasPoa.Add(new LineaPoa
            {
                Nombre = "Linea B del lote en las 4 tablas", Programa = "Ambiente", Ejercicio = 2099,
                IdImportacion = idLoteEnTodasLasTablas,
            });
            ctx.PagosGasto.Add(new PagoGasto
            {
                Gasto = gastoTodas, Fecha = fechaPagoTodas, Monto = 900m, IdImportacion = idLoteEnTodasLasTablas,
            });

            // Lote con DOS LogAuditoria Accion=42 para el mismo IdLote (Menor 5): documenta/fija
            // el comportamiento actual -- DISTINCT ON ("IdLote") ... ORDER BY "IdLote", "Fecha"
            // (ascendente) se queda con la confirmación MÁS TEMPRANA de las dos.
            idLoteDobleLogConfirmacion = Guid.NewGuid();
            ctx.Gastos.Add(new Gasto
            {
                Proveedor = proveedor, Detalle = "Gasto con doble log de confirmación",
                Fecha = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), MontoTotal = 150m,
                FuenteFinanciamiento = fuente, RubroGasto = rubro, CondicionPago = CondicionPago.Contado,
                IdImportacion = idLoteDobleLogConfirmacion,
            });
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = usuarioId, Fecha = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                Accion = (AccionAuditada)AccionImportacionPlanillas, Entidad = "Importacion", EntidadId = 2029,
                IdLote = idLoteDobleLogConfirmacion, Detalle = "Confirmación temprana (gana)",
            });
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = usuarioId, Fecha = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
                Accion = (AccionAuditada)AccionImportacionPlanillas, Entidad = "Importacion", EntidadId = 2030,
                IdLote = idLoteDobleLogConfirmacion, Detalle = "Confirmación tardía (pierde)",
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
            // Minor 5 (review): antes solo se comprobaba NotNull -- fija el valor exacto, así el
            // ORDER BY "Fecha" DESC del CTE "reversion" (la más reciente de las reversiones)
            // queda realmente verificado.
            Assert.Equal(fechaReversionEsperada, lotes[idLoteRevertido].RevertidaEn);
            Assert.Equal(usuarioId, lotes[idLoteRevertido].RevertidaPorUsuarioId);

            // Caso difícil: reconstruido con autoría/ejercicio desconocidos (decisión del
            // usuario), nunca invisible ni bloqueando la migración.
            Assert.True(lotes.ContainsKey(idLoteHuerfano));
            Assert.Null(lotes[idLoteHuerfano].UsuarioId);
            Assert.Null(lotes[idLoteHuerfano].Ejercicio);
            Assert.Null(lotes[idLoteHuerfano].RevertidaEn);

            // Menor 5: lote reconstruido solo desde LineasPoa -- sin confirmacion (Ejercicio sale
            // de "ed") y sin fecha_derivada (LineasPoa no aporta a esa UNION ALL) ⇒ fallback
            // 1970-01-01 (Menor 6: con offset +00 explícito).
            Assert.True(lotes.ContainsKey(idLoteSoloLineaPoa));
            Assert.Null(lotes[idLoteSoloLineaPoa].UsuarioId);
            Assert.Equal(2027, lotes[idLoteSoloLineaPoa].Ejercicio);
            Assert.Equal(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), lotes[idLoteSoloLineaPoa].Fecha);
            Assert.Null(lotes[idLoteSoloLineaPoa].RevertidaEn);

            // Menor 5: lote reconstruido solo desde PagosGasto (el Gasto dueño es una carga
            // manual sin IdImportacion) -- Fecha derivada del propio pago, Ejercicio desconocido
            // (no hay LineaPoa ni confirmacion).
            Assert.True(lotes.ContainsKey(idLoteSoloPago));
            Assert.Null(lotes[idLoteSoloPago].Ejercicio);
            Assert.Equal(fechaPagoHuerfanoEsperada, lotes[idLoteSoloPago].Fecha);

            // Menor 5: lote presente en las 4 tablas a la vez, sin confirmacion -- "guids" (UNION)
            // no lo duplicó pese a aparecer 4 veces, fecha_derivada tomó el MIN real entre
            // Gasto/Ingreso/Pago, y el Ejercicio salió de LineasPoa (ed). Con DOS LineaPoa del
            // mismo IdImportacion, el ORDER BY "IdImportacion", "Id" (fix Menor 2) hace que gane
            // determinísticamente la de Id más chico (2028), no la de Ejercicio más grande (2099).
            Assert.True(lotes.ContainsKey(idLoteEnTodasLasTablas));
            Assert.Equal(2028, lotes[idLoteEnTodasLasTablas].Ejercicio);
            Assert.Equal(fechaIngresoTodasEsperada, lotes[idLoteEnTodasLasTablas].Fecha);

            // Menor 5: dos LogAuditoria Accion=42 para el mismo IdLote -- gana la confirmación
            // MÁS TEMPRANA (DISTINCT ON ... ORDER BY "IdLote", "Fecha" ascendente).
            Assert.True(lotes.ContainsKey(idLoteDobleLogConfirmacion));
            Assert.Equal(2029, lotes[idLoteDobleLogConfirmacion].Ejercicio);

            // ListarHistorialAsync (Bloqueante 2: antes de este fix, un lote sin fila en
            // LotesImportacion desaparecía en silencio de este historial) tiene que devolver, como
            // mínimo, los 3 lotes originales del caso base.
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
    /// MENOR 4 del review adversarial (segunda pasada): el comentario del backfill (Up(), líneas
    /// 47-56 de la migración) dice que "ON CONFLICT DO NOTHING" cubre el caso de "bajar SOLO
    /// hasta AgregaLotesImportacion y volver a subir" -- pero ningún test lo ejercitaba.
    /// DownYUpDeAgregaLotesImportacion_SeAutoRepara baja hasta ANTES de AgregaLotesImportacion,
    /// que hace DropTable, así que en ese test el INSERT siempre corre contra una tabla vacía y
    /// el ON CONFLICT nunca tiene nada con qué chocar. Este test baja SOLO hasta
    /// AgregaLotesImportacion (la tabla y sus filas sobreviven -- Down() de AgregaFksLotesImportacion
    /// solo quita las 4 FK) y sube de nuevo: el INSERT se reintenta sobre una fila que YA existe.
    /// Prueba dos cosas: que no revienta con 23505 (PK duplicada -- es DO NOTHING, no un INSERT
    /// pelado), y que NO pisa el estado real que la app haya escrito después del primer backfill
    /// (acá, una reversión real) con los NULL que el backfill volvería a calcular si reconstruyera
    /// la fila de cero -- si el comentario mintiera y esto fuera en verdad DO UPDATE, o si faltara
    /// el ON CONFLICT directamente, este test lo atraparía.
    /// </summary>
    [Fact]
    public async Task ReaplicarAgregaFksLotesImportacion_SobreFilaYaExistente_OnConflictDoNothingEsIdempotente()
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
                NombreUsuario = "backfill-idempotencia",
                HashContrasena = "hash",
                Rol = RolUsuario.Admin,
                Activo = true,
                FechaAlta = DateTime.UtcNow,
            };
            ctx.Usuarios.Add(usuario);
            await ctx.SaveChangesAsync();
            usuarioId = usuario.Id;

            var proveedor = new Proveedor { Nombre = "Proveedor Idempotencia" };
            var fuente = new FuenteFinanciamiento { Nombre = "Fuente Idempotencia" };
            var rubro = new RubroGasto { Codigo = 3, Nombre = "Rubro Idempotencia" };
            ctx.Proveedores.Add(proveedor);
            ctx.FuentesFinanciamiento.Add(fuente);
            ctx.RubrosGasto.Add(rubro);
            await ctx.SaveChangesAsync();

            idLote = Guid.NewGuid();
            var fecha = new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);
            ctx.Gastos.Add(new Gasto
            {
                Proveedor = proveedor, Detalle = "Gasto idempotencia", Fecha = fecha, MontoTotal = 250m,
                FuenteFinanciamiento = fuente, RubroGasto = rubro, CondicionPago = CondicionPago.Contado,
                IdImportacion = idLote,
            });
            ctx.LogsAuditoria.Add(new LogAuditoria
            {
                UsuarioId = usuarioId, Fecha = fecha, Accion = (AccionAuditada)AccionImportacionPlanillas,
                Entidad = "Importacion", EntidadId = 2026, IdLote = idLote, Detalle = "Confirmación idempotencia",
            });

            await ctx.SaveChangesAsync();
        }

        // 1ra pasada a HEAD: el backfill reconstruye idLote desde cero.
        await using (var ctx = CrearContexto(connectionString))
        {
            var migrador = ctx.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrador.MigrateAsync();
        }

        // La app usa el sistema normalmente DESPUÉS del backfill: el lote se revierte de verdad.
        // Es exactamente el estado que un segundo INSERT (si no fuera DO NOTHING) pisaría con los
        // NULL que el backfill volvería a calcular de cero.
        var fechaReversionReal = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
        await using (var ctx = CrearContexto(connectionString))
        {
            var lote = await ctx.LotesImportacion.SingleAsync(l => l.Id == idLote);
            lote.MarcarRevertida(fechaReversionReal, usuarioId);
            await ctx.SaveChangesAsync();
        }

        // Down() SOLO hasta AgregaLotesImportacion (la tabla y sus filas -- incluida la reversión
        // recién marcada -- sobreviven; Down() de AgregaFksLotesImportacion solo quita las 4 FK) +
        // Up() de nuevo: reintenta el INSERT ... ON CONFLICT DO NOTHING contra una fila que YA
        // existe.
        await using (var ctx = CrearContexto(connectionString))
        {
            var migrador = ctx.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrador.MigrateAsync(MigracionAgregaLotesImportacion);

            var excepcion = await Record.ExceptionAsync(() => migrador.MigrateAsync());
            Assert.Null(excepcion);
        }

        await using (var ctx = CrearContexto(connectionString))
        {
            var lotes = await ctx.LotesImportacion.Where(l => l.Id == idLote).ToListAsync();
            Assert.Single(lotes); // ni duplicado ni 23505

            // El ON CONFLICT DO NOTHING no pisó el estado real (la reversión) con lo que el
            // backfill reconstruiría de cero -- si fuera DO UPDATE (o si faltara el ON CONFLICT y
            // este Assert.Null de arriba no hubiera ya fallado), RevertidaEn volvería a NULL acá.
            Assert.Equal(fechaReversionReal, lotes[0].RevertidaEn);
            Assert.Equal(usuarioId, lotes[0].RevertidaPorUsuarioId);
            Assert.Equal(2026, lotes[0].Ejercicio);
            Assert.Equal(usuarioId, lotes[0].UsuarioId);
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
