using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgregaFksLotesImportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill (review adversarial, BLOQUEANTE 1+2 — REPRODUCIDO empíricamente contra un
            // Postgres limpio: sin esto, las 4 AddForeignKey de abajo tiran 23503 apenas hay UN
            // Gasto/IngresoCaja/LineaPoa/PagoGasto con IdImportacion poblado por
            // ImportacionRepository.ConfirmarAsync (columnas que existen desde
            // 20260721153410/20260722105950, mucho antes que LotesImportacion) — y como
            // Program.cs corre MigrateAsync() al arranque de la API, eso es un crash-loop del
            // proceso entero, no un error de una sola corrida.
            //
            // Reconstruye UN LoteImportacion por cada Guid distinto que aparece en las 4 tablas
            // hijas (el driver de "guids", más abajo) — nunca al revés: un Guid que sólo existe
            // en LogsAuditoria (una corrida que no llegó a crear ningún Gasto/Ingreso/Linea/Pago,
            // p.ej. sólo declaró maestros nuevos) no necesita fila acá, no hay ningún FK que
            // vaya a violar.
            //
            // Fecha/UsuarioId/Ejercicio se derivan del LogAuditoria de la confirmación
            // (Accion=42/ImportacionPlanillas, EntidadId=Ejercicio — mismo criterio que
            // ImportacionRepository.ConfirmarAsync escribe hoy). RevertidaEn/RevertidaPorUsuarioId
            // del LogAuditoria de la reversa (Accion=43/ReversionImportacion), si existe.
            //
            // Caso difícil (decisión del usuario, ver review): un Guid huérfano CON hijos pero
            // SIN ningún LogAuditoria de origen (log borrado, o datos migrados a mano) no tiene de
            // dónde derivar autor/ejercicio — antes que perder el vínculo (nulear IdImportacion en
            // los hijos) o fallar la migración entera por un caso de borde, se reconstruye el lote
            // con UsuarioId/Ejercicio en NULL (LoteImportacion.UsuarioId/Ejercicio son nullable
            // desde AgregaLotesImportacion, mismo criterio que CorridaBackup.UsuarioId) — un lote
            // "de autoría desconocida" es preferible a un lote invisible o datos huérfanos. Su
            // Fecha se deriva como el MIN(Fecha) entre sus propios Gastos/IngresosCaja/PagosGasto
            // (LineasPoa no tiene columna Fecha) — nunca null, porque el Guid está en el driver
            // "guids" precisamente porque aparece en al menos una de esas filas. El único caso sin
            // NINGUNA fecha derivable sería un Guid que sólo aparece en LineasPoa sin ningún
            // Gasto/Ingreso/Pago asociado y sin log — ahí se usa 1970-01-01 UTC como último
            // fallback (nunca null: Fecha no es nullable) marcando visualmente un dato reconstruido
            // sin información real de cuándo ocurrió.
            //
            // Idempotente (ON CONFLICT DO NOTHING): si algún día se corre Down() de ESTA migración
            // (solo quita las 4 FK, no borra la tabla ni sus filas — Down() de AgregaLotesImportacion
            // es quien la dropea) y después Up() de nuevo SIN pasar por Down() de
            // AgregaLotesImportacion, este INSERT se reintenta sobre filas que ya existen; sin
            // ON CONFLICT tiraría 23505 (PK duplicada) en vez de ser un no-op. Mismo motivo por el
            // que Down() de esta migración NO revierte el backfill: no hace falta (ver IMPORTANTE 5
            // del review) — un Down()+Up() completo pasando por AgregaLotesImportacion.Down()
            // (DropTable) se auto-repara solo, porque este backfill vuelve a reconstruir TODO desde
            // LogsAuditoria/Gastos/IngresosCaja/LineasPoa/PagosGasto, que esa migración no toca.
            migrationBuilder.Sql(
                """
                INSERT INTO "LotesImportacion" ("Id", "Fecha", "UsuarioId", "Ejercicio", "RevertidaEn", "RevertidaPorUsuarioId")
                WITH guids AS (
                    SELECT "IdImportacion" AS "Id" FROM "Gastos" WHERE "IdImportacion" IS NOT NULL
                    UNION
                    SELECT "IdImportacion" FROM "IngresosCaja" WHERE "IdImportacion" IS NOT NULL
                    UNION
                    SELECT "IdImportacion" FROM "LineasPoa" WHERE "IdImportacion" IS NOT NULL
                    UNION
                    SELECT "IdImportacion" FROM "PagosGasto" WHERE "IdImportacion" IS NOT NULL
                ),
                confirmacion AS (
                    SELECT DISTINCT ON ("IdLote") "IdLote" AS "Id", "Fecha", "UsuarioId", "EntidadId" AS "Ejercicio"
                    FROM "LogsAuditoria"
                    WHERE "Accion" = 42 AND "IdLote" IS NOT NULL
                    ORDER BY "IdLote", "Fecha"
                ),
                reversion AS (
                    SELECT DISTINCT ON ("IdLote") "IdLote" AS "Id", "Fecha" AS "RevertidaEn", "UsuarioId" AS "RevertidaPorUsuarioId"
                    FROM "LogsAuditoria"
                    WHERE "Accion" = 43 AND "IdLote" IS NOT NULL
                    ORDER BY "IdLote", "Fecha" DESC
                ),
                fecha_derivada AS (
                    SELECT g."Id", MIN(f."Fecha") AS "Fecha"
                    FROM guids g
                    JOIN (
                        SELECT "IdImportacion" AS "Id", "Fecha" FROM "Gastos" WHERE "IdImportacion" IS NOT NULL
                        UNION ALL
                        SELECT "IdImportacion", "Fecha" FROM "IngresosCaja" WHERE "IdImportacion" IS NOT NULL
                        UNION ALL
                        SELECT "IdImportacion", "Fecha" FROM "PagosGasto" WHERE "IdImportacion" IS NOT NULL
                    ) f ON f."Id" = g."Id"
                    GROUP BY g."Id"
                ),
                ejercicio_derivado AS (
                    SELECT DISTINCT ON ("IdImportacion") "IdImportacion" AS "Id", "Ejercicio"
                    FROM "LineasPoa"
                    WHERE "IdImportacion" IS NOT NULL
                )
                SELECT
                    g."Id",
                    COALESCE(c."Fecha", fd."Fecha", TIMESTAMPTZ '1970-01-01') AS "Fecha",
                    c."UsuarioId" AS "UsuarioId",
                    COALESCE(c."Ejercicio", ed."Ejercicio") AS "Ejercicio",
                    r."RevertidaEn",
                    r."RevertidaPorUsuarioId"
                FROM guids g
                LEFT JOIN confirmacion c ON c."Id" = g."Id"
                LEFT JOIN reversion r ON r."Id" = g."Id"
                LEFT JOIN fecha_derivada fd ON fd."Id" = g."Id"
                LEFT JOIN ejercicio_derivado ed ON ed."Id" = g."Id"
                ON CONFLICT ("Id") DO NOTHING;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Gastos_LotesImportacion_IdImportacion",
                table: "Gastos",
                column: "IdImportacion",
                principalTable: "LotesImportacion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IngresosCaja_LotesImportacion_IdImportacion",
                table: "IngresosCaja",
                column: "IdImportacion",
                principalTable: "LotesImportacion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LineasPoa_LotesImportacion_IdImportacion",
                table: "LineasPoa",
                column: "IdImportacion",
                principalTable: "LotesImportacion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PagosGasto_LotesImportacion_IdImportacion",
                table: "PagosGasto",
                column: "IdImportacion",
                principalTable: "LotesImportacion",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A propósito NO revierte el backfill (no DELETE de las filas insertadas por Up()):
            // simétrico en el sentido que importa (deshace TODO lo que este archivo pudo romper —
            // las 4 constraints), no en el de "dejar la base bit a bit igual a antes de Up()". Las
            // filas de LotesImportacion son inertes sin las FK: no bloquean nada, no rompen nada
            // corriendo Down() solo. Un Down()+Up() completo que pase por
            // AgregaLotesImportacion.Down() (DropTable) sí las borra, y el backfill de arriba las
            // reconstruye solo al volver a correr Up() — ver comentario largo de Up() y IMPORTANTE
            // 5 del review.
            migrationBuilder.DropForeignKey(
                name: "FK_Gastos_LotesImportacion_IdImportacion",
                table: "Gastos");

            migrationBuilder.DropForeignKey(
                name: "FK_IngresosCaja_LotesImportacion_IdImportacion",
                table: "IngresosCaja");

            migrationBuilder.DropForeignKey(
                name: "FK_LineasPoa_LotesImportacion_IdImportacion",
                table: "LineasPoa");

            migrationBuilder.DropForeignKey(
                name: "FK_PagosGasto_LotesImportacion_IdImportacion",
                table: "PagosGasto");
        }
    }
}
