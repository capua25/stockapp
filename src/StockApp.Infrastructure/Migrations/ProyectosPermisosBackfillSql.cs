namespace StockApp.Infrastructure.Migrations;

/// <summary>
/// SQL de backfill de los permisos de Proyectos (spec 2026-10-30) para los Operadores
/// preexistentes, compartido entre la migración AgregaProyectos y su test. Mismo patrón que
/// PermisoUsuarioBackfillSql, que NO se modifica (es historia). Los Admin no reciben filas:
/// cortocircuitan los permisos.
/// </summary>
public static class ProyectosPermisosBackfillSql
{
    public const string InsertarPermisosProyectos =
        """
        INSERT INTO "PermisosUsuario" ("UsuarioId", "Permiso")
        SELECT u."Id", p.permiso
        FROM "Usuarios" u
        CROSS JOIN (VALUES
            ('proyectos.ver'), ('proyectos.gestionar')
        ) AS p(permiso)
        WHERE u."Rol" = 1
        ON CONFLICT ("UsuarioId", "Permiso") DO NOTHING;
        """;
}
